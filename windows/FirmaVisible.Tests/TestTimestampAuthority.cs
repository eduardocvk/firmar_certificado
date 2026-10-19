using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using FirmaVisible.Core;

internal sealed class TestTimestampAuthority : IDisposable
{
    private readonly RSA rootKey = RSA.Create(2048), key = RSA.Create(2048);
    public X509Certificate2 Root { get; }
    private readonly X509Certificate2 certificate;
    public TestTimestampAuthority()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1); var after = DateTimeOffset.UtcNow.AddDays(1);
        var rootRequest = new CertificateRequest("CN=Test TSA Root - NO REAL", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        Root = rootRequest.CreateSelfSigned(before, after);
        var request = new CertificateRequest("CN=Test Timestamp - NO REAL", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.8") }, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var publicCertificate = request.Create(Root, before, after, RandomNumberGenerator.GetBytes(16));
        certificate = publicCertificate.CopyWithPrivateKey(key);
    }
    public TimestampClient Client() => new(bytes => Respond(bytes), Root);
    public byte[] Respond(byte[] encoded, bool wrongNonce = false, bool wrongHash = false)
    {
        if (!Rfc3161TimestampRequest.TryDecode(encoded, out var request, out _)) throw new Exception("Solicitud de prueba no válida.");
        byte[]? nonce = request.GetNonce()?.ToArray();
        if (wrongNonce && nonce is not null) nonce[^1] ^= 1;
        var info = new Rfc3161TimestampTokenInfo(new Oid("1.2.3.4.1"), request.HashAlgorithmId,
            wrongHash ? new byte[32] : request.GetMessageHash(), new byte[] { 1 }, DateTimeOffset.UtcNow,
            null, false, nonce is null ? null : new ReadOnlyMemory<byte>(nonce), null, null);
        var cms = new SignedCms(new ContentInfo(new Oid("1.2.840.113549.1.9.16.1.4"), info.Encode()), false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        { DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), IncludeOption = X509IncludeOption.EndCertOnly };
        signer.Certificates.Add(Root);
        var ess = new AsnWriter(AsnEncodingRules.DER);
        ess.PushSequence(); ess.PushSequence(); ess.PushSequence(); ess.WriteOctetString(SHA256.HashData(certificate.RawData)); ess.PopSequence(); ess.PopSequence(); ess.PopSequence();
        var oid = new Oid("1.2.840.113549.1.9.16.2.47");
        signer.SignedAttributes.Add(new CryptographicAttributeObject(oid, new AsnEncodedDataCollection(new AsnEncodedData(oid, ess.Encode()))));
        cms.ComputeSignature(signer);
        var response = new AsnWriter(AsnEncodingRules.DER); response.PushSequence(); response.PushSequence(); response.WriteInteger(0); response.PopSequence(); response.WriteEncodedValue(cms.Encode()); response.PopSequence();
        return response.Encode();
    }
    public void Dispose() { certificate.Dispose(); Root.Dispose(); key.Dispose(); rootKey.Dispose(); }
}
