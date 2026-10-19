using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using iText.Commons.Digest;
using iText.Signatures;

namespace FirmaVisible.Core;

/// <summary>RFC3161 with request binding, exclusive TSA EKU and a private PKIX trust anchor. No revocation/qualification assertion.</summary>
public sealed class TimestampClient : ITSAClient
{
    public const string Endpoint = "http://timestamp.digicert.com";
    public const string TimestampOid = "1.2.840.113549.1.9.16.2.14";
    private readonly Func<byte[], byte[]> exchange;
    private readonly byte[] anchor;

    public TimestampClient(Func<byte[], byte[]> exchange, X509Certificate2 root)
    { this.exchange = exchange; anchor = root.RawData; }

    public static TimestampClient DigiCert()
    {
        using var stream = typeof(TimestampClient).Assembly.GetManifestResourceStream("FirmaVisible.TsaRoot.cer")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        using var root = X509CertificateLoader.LoadCertificate(bytes.ToArray());
        return new(Post, root);
    }

    private static byte[] Post(byte[] request)
    {
        // The official endpoint is HTTP. Authenticity comes from the nonce,
        // imprint, token signature and pinned chain, never from this transport.
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None };
        using var client = new HttpClient(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new ByteArrayContent(request) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/timestamp-reply"));
        using var response = client.Send(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode != HttpStatusCode.OK) throw new IOException($"La autoridad de sellado no está disponible (HTTP {(int)response.StatusCode}).");
        if (response.Content.Headers.ContentType?.MediaType != "application/timestamp-reply") throw new IOException("Respuesta inesperada de la autoridad de sellado.");
        using var input = response.Content.ReadAsStream(timeout.Token);
        using var output = new MemoryStream(); byte[] buffer = new byte[8192]; int count;
        while ((count = input.ReadAsync(buffer, timeout.Token).AsTask().GetAwaiter().GetResult()) != 0)
        { if (output.Length + count > 128 * 1024) throw new IOException("Respuesta de sellado excesiva."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }

    public int GetTokenSizeEstimate() => 32768;
    public IMessageDigest GetMessageDigest() => new BouncyCastleDigest().GetMessageDigest("SHA-256");

    public byte[] GetTimeStampToken(byte[] imprint)
    {
        if (imprint.Length != 32) throw new CryptographicException("Se requiere una huella SHA-256 para el sello.");
        var request = Rfc3161TimestampRequest.CreateFromHash(imprint, HashAlgorithmName.SHA256,
            nonce: RandomNumberGenerator.GetBytes(20), requestSignerCertificates: true);
        byte[] response = exchange(request.Encode());
        if (response.Length > 128 * 1024) throw new IOException("Respuesta de sellado excesiva.");
        var token = request.ProcessResponse(response, out int consumed);
        if (consumed != response.Length) throw new CryptographicException("Datos ajenos a la respuesta de sellado.");
        if (!token.VerifySignatureForHash(imprint, HashAlgorithmName.SHA256, out var signer) || signer is null)
            throw new CryptographicException("El sello no corresponde a esta firma o su firma no es correcta.");
        var eku = signer.Extensions.OfType<X509EnhancedKeyUsageExtension>().SingleOrDefault();
        if (eku is null || !eku.Critical || eku.EnhancedKeyUsages.Count != 1 || eku.EnhancedKeyUsages[0].Value != "1.3.6.1.5.5.7.3.8")
            throw new CryptographicException("El certificado no es exclusivo de sellado de tiempo.");
        if ((DateTimeOffset.UtcNow - token.TokenInfo.Timestamp).Duration() > TimeSpan.FromMinutes(10))
            throw new IOException("El reloj del ordenador difiere de la autoridad de sellado. Revisa la hora y vuelve a intentarlo.");
        using var root = X509CertificateLoader.LoadCertificate(anchor);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.ExtraStore.AddRange(token.AsSignedCms().Certificates);
        chain.ChainPolicy.VerificationTime = token.TokenInfo.Timestamp.UtcDateTime;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        if (!chain.Build(signer)) throw new CryptographicException("La cadena de la autoridad de sellado no alcanza la raíz de confianza esperada.");
        return token.AsSignedCms().Encode();
    }

    /// <summary>Cryptographic timestamp integrity only, without establishing trust for arbitrary external PDF timestamps.</summary>
    public static DateTimeOffset? VerifyEmbedded(byte[] cmsBytes)
    {
        var cms = new SignedCms(); cms.Decode(cmsBytes);
        DateTimeOffset? latest = null;
        foreach (SignerInfo signer in cms.SignerInfos)
        {
            var attributes = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Where(a => a.Oid.Value == TimestampOid).ToArray();
            if (attributes.Length == 0) continue;
            if (attributes.Length != 1 || attributes[0].Values.Count != 1) throw new CryptographicException("Sello de tiempo mal formado.");
            byte[] bytes = attributes[0].Values[0].RawData;
            if (!Rfc3161TimestampToken.TryDecode(bytes, out var token, out int consumed) || consumed != bytes.Length ||
                !token.VerifySignatureForSignerInfo(signer, out _)) throw new CryptographicException("La integridad del sello de tiempo o su enlace con la firma no es correcta.");
            latest = token.TokenInfo.Timestamp;
        }
        return latest;
    }
}
