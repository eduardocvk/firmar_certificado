using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Signatures;

namespace FirmaVisible.Core;

/// <summary>Uses CNG/CSP or the ephemeral PFX key through .NET, without exporting it.</summary>
internal sealed class WindowsKeySignature(CertificateIdentity identity) : IExternalSignature
{
    public string GetDigestAlgorithmName() => "SHA-256";
    public string GetSignatureAlgorithmName()
    {
        using var rsa = identity.Certificate.GetRSAPublicKey();
        return rsa is null ? "ECDSA" : "RSA";
    }
    public ISignatureMechanismParams? GetSignatureMechanismParameters() => null;
    public byte[] Sign(byte[] message)
    {
        using var rsa = identity.Certificate.GetRSAPrivateKey();
        if (rsa is not null) return rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ec = identity.Certificate.GetECDsaPrivateKey()
            ?? throw new CryptographicException("No se pudo acceder a la clave privada.");
        return ec.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }
}
