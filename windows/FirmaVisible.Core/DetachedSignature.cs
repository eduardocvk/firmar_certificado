using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using iText.Signatures;

namespace FirmaVisible.Core;

/// <summary>CAdES-BES detached signatures; the original is required for verification.</summary>
public static class DetachedSignature
{
    public const long MaximumFileSize=100L*1024*1024;
    public const long MaximumSignatureSize=4L*1024*1024;
    public const string SigningCertificateV2Oid="1.2.840.113549.1.9.16.2.47";
    private static byte[] ReadBounded(string path,long maximum)
    {
        using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(file.Length>maximum)throw new IOException($"El archivo supera el límite de {maximum/1024/1024} MiB para esta operación.");
        byte[] bytes=new byte[checked((int)file.Length)];file.ReadExactly(bytes);return bytes;
    }
    public static void Sign(string original,string destination,CertificateIdentity identity,ITSAClient? tsa=null)
    {
        identity.CheckUsable();
        if(StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(original),Path.GetFullPath(destination))||File.Exists(destination))throw new IOException("Se conservarán el original y los destinos existentes. Elige otro destino.");
        byte[] content=ReadBounded(original,MaximumFileSize);
        var cms=new SignedCms(new ContentInfo(content),detached:true);
        var signer=new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber,identity.Certificate){DigestAlgorithm=new Oid("2.16.840.1.101.3.4.2.1"),IncludeOption=X509IncludeOption.EndCertOnly};
        var certificates=new List<X509Certificate2>();
        try
        {
            foreach(byte[] bytes in identity.GetPublicChain()) { var cert=X509CertificateLoader.LoadCertificate(bytes);certificates.Add(cert);signer.Certificates.Add(cert); }
            signer.SignedAttributes.Add(new Pkcs9SigningTime(DateTime.UtcNow));
            var attribute=new AsnWriter(AsnEncodingRules.DER);
            using(attribute.PushSequence())using(attribute.PushSequence())using(attribute.PushSequence())attribute.WriteOctetString(SHA256.HashData(identity.Certificate.RawData));
            signer.SignedAttributes.Add(new AsnEncodedData(new Oid(SigningCertificateV2Oid),attribute.Encode()));
            cms.ComputeSignature(signer,silent:false);
            if(tsa is not null)
            {
                byte[] token=tsa.GetTimeStampToken(SHA256.HashData(cms.SignerInfos[0].GetSignature()));
                cms.SignerInfos[0].AddUnsignedAttribute(new AsnEncodedData(new Oid(TimestampClient.TimestampOid),token));
            }
            cms.CheckSignature(verifySignatureOnly:true);
            byte[] encoded=cms.Encode();
            if(tsa is not null&&TimestampClient.VerifyEmbedded(encoded) is null)throw new CryptographicException("Falta el sello de tiempo solicitado.");
            // A temporary result is verified before a no-replace rename publishes it.
            string temporary=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!,".firmapdf-cms-"+Guid.NewGuid().ToString("N"));
            try { File.WriteAllBytes(temporary,encoded);Verify(original,temporary);File.Move(temporary,destination,overwrite:false); }
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        finally {foreach(var certificate in certificates)certificate.Dispose();}
    }
    public static SignedCms ReadVerified(string original,string signature)
    {
        var cms=new SignedCms(new ContentInfo(ReadBounded(original,MaximumFileSize)),detached:true);
        cms.Decode(ReadBounded(signature,MaximumSignatureSize));
        if(!cms.Detached||cms.SignerInfos.Count==0)throw new CryptographicException("Se requiere una firma CMS separada (.p7s) y su archivo original.");
        cms.CheckSignature(verifySignatureOnly:true);
        foreach(SignerInfo signer in cms.SignerInfos)CheckCertificateBinding(signer);
        return cms;
    }
    private static void CheckCertificateBinding(SignerInfo signer)
    {
        var attributes=signer.SignedAttributes.Cast<CryptographicAttributeObject>().Where(a=>a.Oid.Value==SigningCertificateV2Oid).ToArray();
        if(attributes.Length==0)return; // External basic CMS need not contain ESSCertIDv2.
        if(attributes.Length!=1||attributes[0].Values.Count!=1||signer.Certificate is null)throw new CryptographicException("Referencia de certificado CAdES mal formada.");
        var reader=new AsnReader(attributes[0].Values[0].RawData,AsnEncodingRules.BER);
        var outer=reader.ReadSequence();var certs=outer.ReadSequence();var ess=certs.ReadSequence();
        string hashOid="2.16.840.1.101.3.4.2.1";
        if(ess.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence)){var algorithm=ess.ReadSequence();hashOid=algorithm.ReadObjectIdentifier();if(algorithm.HasData)algorithm.ReadNull();algorithm.ThrowIfNotEmpty();}
        byte[] expected=ess.ReadOctetString();
        byte[] actual=hashOid switch {"2.16.840.1.101.3.4.2.1"=>SHA256.HashData(signer.Certificate.RawData),"2.16.840.1.101.3.4.2.2"=>SHA384.HashData(signer.Certificate.RawData),"2.16.840.1.101.3.4.2.3"=>SHA512.HashData(signer.Certificate.RawData),_=>throw new CryptographicException("Algoritmo de referencia CAdES no admitido.")};
        if(!CryptographicOperations.FixedTimeEquals(expected,actual))throw new CryptographicException("La firma CAdES hace referencia a otro certificado.");
    }
    public static Verification Verify(string original,string signature)
    {
        return Summarize(ReadVerified(original,signature));
    }
    internal static Verification Summarize(SignedCms cms)
    {
        int stamps=0;DateTimeOffset? latest=null;
        foreach(SignerInfo signer in cms.SignerInfos)
            if(signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Any(a=>a.Oid.Value==TimestampClient.TimestampOid))stamps++;
        latest=TimestampClient.VerifyEmbedded(cms.Encode());
        return new(cms.SignerInfos.Count,true,stamps,latest);
    }
}
