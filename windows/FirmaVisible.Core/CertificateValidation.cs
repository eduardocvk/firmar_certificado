using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FirmaVisible.Core;

public enum RevocationResult { Good,Revoked,Unknown,NotChecked }
public sealed record CertificateCheck(string Name,string Fingerprint,bool Trusted,bool InDate,RevocationResult Revocation,string Details,DateTimeOffset CheckedAt)
{
    public override string ToString()=> $"{Name}\nHuella SHA-256: {Fingerprint}\nConfianza de la cadena: {(Trusted?"aceptada":"no aceptada")}\nVigencia actual: {(InDate?"dentro de plazo":"fuera de plazo")}\nRevocación: {Revocation switch {RevocationResult.Good=>"comprobada; no consta revocación",RevocationResult.Revoked=>"CERTIFICADO REVOCADO",RevocationResult.Unknown=>"no se ha podido determinar; no equivale a válido",_=>"no comprobada"}}\n{Details}";
}
public static class CertificateValidation
{
    public static CertificateCheck Check(X509Certificate2 certificate,X509Certificate2Collection? extra=null,bool online=true)
        =>CheckCore(certificate,extra,online,null);
    // Isolated custom-root validation supports tests without modifying Windows trust stores.
    internal static CertificateCheck CheckCore(X509Certificate2 certificate,X509Certificate2Collection? extra,bool online,X509Certificate2Collection? roots)
    {
        var now=DateTimeOffset.UtcNow;
        using var trust=CreateChain(extra,roots,now,X509RevocationMode.NoCheck,online);
        bool trusted=trust.Build(certificate);
        var revocation=RevocationResult.NotChecked;
        var messages=new List<string>();
        if(!trusted)messages.AddRange(trust.ChainStatus.Select(s=>s.Status+": "+s.StatusInformation.Trim()));
        if(online)
        {
            using var checkedChain=CreateChain(extra,roots,now,X509RevocationMode.Online,true);
            bool accepted=checkedChain.Build(certificate);
            var flags=checkedChain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError,(value,status)=>value|status.Status);
            revocation=(flags&X509ChainStatusFlags.Revoked)!=0?RevocationResult.Revoked:
                accepted&&checkedChain.ChainElements.Count>1?RevocationResult.Good:RevocationResult.Unknown;
            messages.AddRange(checkedChain.ChainStatus.Select(s=>s.Status+": "+s.StatusInformation.Trim()));
        }
        string source=roots is null?"Confianza según el almacén de Windows.":"Confianza de prueba aislada; no modifica Windows.";
        string details=source+"\n"+string.Join("\n",messages.Distinct());
        return new(certificate.GetNameInfo(X509NameType.SimpleName,false),certificate.GetCertHashString(HashAlgorithmName.SHA256),trusted,now.UtcDateTime>=certificate.NotBefore.ToUniversalTime()&&now.UtcDateTime<=certificate.NotAfter.ToUniversalTime(),revocation,details.Trim(),now);
    }
    private static X509Chain CreateChain(X509Certificate2Collection? extra,X509Certificate2Collection? roots,DateTimeOffset now,X509RevocationMode mode,bool downloads)
    {
        var chain=new X509Chain();var policy=chain.ChainPolicy;
        policy.TrustMode=roots is null?X509ChainTrustMode.System:X509ChainTrustMode.CustomRootTrust;
        if(roots is not null)policy.CustomTrustStore.AddRange(roots);
        if(extra is not null)policy.ExtraStore.AddRange(extra);
        policy.VerificationFlags=X509VerificationFlags.NoFlag;policy.VerificationTime=now.UtcDateTime;
        policy.RevocationMode=mode;policy.RevocationFlag=X509RevocationFlag.ExcludeRoot;
        policy.UrlRetrievalTimeout=TimeSpan.FromSeconds(8);policy.DisableCertificateDownloads=!downloads;
        return chain;
    }
}
