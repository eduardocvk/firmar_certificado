using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FirmaVisible.Core;

public sealed class CertificateIdentity : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public string Name => Certificate.GetNameInfo(X509NameType.SimpleName, false);
    public string Fingerprint => Certificate.GetCertHashString(HashAlgorithmName.SHA256);
    public string Description => $"{Name} · caduca {Certificate.NotAfter:dd/MM/yyyy}";
    public string Details => $"{Description}\nEmisor: {Certificate.GetNameInfo(X509NameType.SimpleName, true)}\nHuella SHA-256: {Certificate.GetCertHashString(HashAlgorithmName.SHA256)}";
    private readonly IReadOnlyList<X509Certificate2> suppliedChain;

    public CertificateIdentity(X509Certificate2 certificate, IReadOnlyList<X509Certificate2>? chain = null)
    {
        if (!certificate.HasPrivateKey) throw new CryptographicException("El certificado no contiene su clave privada.");
        Certificate = certificate;
        suppliedChain = chain ?? [];
    }

    public void CheckUsable()
    {
        DateTime now = DateTime.Now;
        if (now < Certificate.NotBefore || now > Certificate.NotAfter)
            throw new CryptographicException("El certificado está caducado o todavía no es válido. Comprueba también el reloj del equipo.");
        var usage = Certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage is not null && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
            throw new CryptographicException("El certificado no permite firmar documentos.");
        using RSA? rsa = Certificate.GetRSAPublicKey();
        if (rsa is not null)
        {
            if (rsa.KeySize < 2048) throw new CryptographicException("La clave RSA debe tener al menos 2048 bits.");
            return;
        }
        using ECDsa? ec = Certificate.GetECDsaPublicKey();
        if (ec is null || ec.KeySize < 256)
            throw new CryptographicException("Solo se admiten RSA de 2048 bits o superior y EC de 256 bits o superior.");
    }

    public IReadOnlyList<byte[]> GetPublicChain()
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        foreach (var cert in suppliedChain) chain.ChainPolicy.ExtraStore.Add(cert);
        // This only collects available certificates. It does NOT establish trust.
        chain.Build(Certificate);
        return chain.ChainElements.Count == 0 ? [Certificate.RawData] : chain.ChainElements.Select(e => e.Certificate.RawData).ToArray();
    }

    public static List<CertificateIdentity> FromWindowsStore()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var result = new List<CertificateIdentity>();
        foreach (var cert in store.Certificates)
        {
            if (cert.HasPrivateKey) result.Add(new(cert));
            else cert.Dispose();
        }
        if (result.Count == 0) throw new CryptographicException("No se encontraron certificados con clave privada en tu almacén de Windows.");
        return result.OrderBy(c => c.Name).ThenByDescending(c => c.Certificate.NotAfter).ToList();
    }

    public static CertificateIdentity? FindInWindowsStore(string fingerprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        CertificateIdentity? found = null;
        foreach (var cert in store.Certificates)
        {
            if (found is null && cert.HasPrivateKey && string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), fingerprint, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = new CertificateIdentity(cert);
                try { candidate.CheckUsable(); found = candidate; }
                catch (CryptographicException) { candidate.Dispose(); }
            }
            else cert.Dispose();
        }
        return found;
    }

    public static List<CertificateIdentity> FromPkcs12(string path, ReadOnlySpan<char> password)
    {
        var certs = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
        var chain = certs.Cast<X509Certificate2>().ToArray();
        var identities = chain.Where(c => c.HasPrivateKey).Select(c => new CertificateIdentity(c, chain)).ToList();
        if (identities.Count == 0)
        {
            foreach (var cert in chain) cert.Dispose();
            throw new CryptographicException("El archivo no contiene un certificado con su clave privada.");
        }
        return identities;
    }

    public override string ToString() => Description;
    public void Dispose()
    {
        Certificate.Dispose();
        foreach (var cert in suppliedChain) if (!ReferenceEquals(cert, Certificate)) cert.Dispose();
    }
}
