using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using iText.Kernel.Pdf;
using iText.Signatures;

namespace FirmaVisible.Core;

public sealed record SignatureInspection(Verification Integrity,IReadOnlyList<(string Role,CertificateCheck Check)> Certificates)
{
    public override string ToString()=> $"Integridad criptográfica: correcta. Firmas: {Integrity.Count}.\n"+
        (Integrity.LatestCoversWholeFile?"La última firma cubre el archivo completo.":"Hay contenido posterior a la última firma.")+"\n"+
        $"Sellos de tiempo íntegros: {Integrity.TimestampCount}."+(Integrity.LatestTimestamp is {} time?$" Último: {time.ToLocalTime():dd/MM/yyyy HH:mm:ss zzz}":"")+"\n\n"+
        string.Join("\n\n",Certificates.Select(c=>c.Role+"\n"+c.Check))+"\n\n"+
        "Comprobación de certificados a fecha actual: "+DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss zzz")+". Windows puede usar respuestas de revocación en caché.\nNo acredita cualificación eIDAS ni validez histórica; no incorpora evidencias LT/LTA.";
}
public static class SignatureInspector
{
    public static SignatureInspection InspectPdf(string path,bool online=true)
    {
        using var guard=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        var integrity=PdfEngine.Verify(path);
        if(integrity.Count==0)throw new InvalidDataException("El PDF no contiene firmas digitales.");
        var checks=new List<(string,CertificateCheck)>();
        using var reader=new PdfReader(path);using var document=new PdfDocument(reader);var signatures=new SignatureUtil(document);
        foreach(string name in signatures.GetSignatureNames())
        {
            var cms=new SignedCms();cms.Decode(signatures.GetSignatureDictionary(name).GetAsString(PdfName.Contents).GetValueBytes());
            InspectCms(cms,name,online,checks);
        }
        return new(integrity,checks);
    }
    public static SignatureInspection InspectDetached(string original,string signature,bool online=true)
    {
        using var originalGuard=new FileStream(original,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var signatureGuard=new FileStream(signature,FileMode.Open,FileAccess.Read,FileShare.Read);
        var cms=DetachedSignature.ReadVerified(original,signature);
        var checks=new List<(string,CertificateCheck)>();InspectCms(cms,"Firma separada",online,checks);
        return new(DetachedSignature.Summarize(cms),checks);
    }
    private static void InspectCms(SignedCms cms,string label,bool online,List<(string,CertificateCheck)> checks)
    {
        foreach(SignerInfo signer in cms.SignerInfos)
        {
            if(signer.Certificate is not {} certificate)throw new CryptographicException("Falta el certificado del firmante.");
            checks.Add((label,CertificateValidation.Check(certificate,cms.Certificates,online)));
            foreach(var attribute in signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Where(a=>a.Oid.Value==TimestampClient.TimestampOid))
            {
                if(attribute.Values.Count!=1||!Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData,out var token,out int consumed)||consumed!=attribute.Values[0].RawData.Length||!token.VerifySignatureForSignerInfo(signer,out var tsa)||tsa is null)
                    throw new CryptographicException("El sello no corresponde a la firma o está alterado.");
                checks.Add((label+" · autoridad de sellado (estado actual)",CertificateValidation.Check(tsa,token.AsSignedCms().Certificates,online)));
            }
        }
    }
}
