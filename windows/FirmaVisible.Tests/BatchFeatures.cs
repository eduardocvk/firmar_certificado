using System.IO;
using FirmaVisible.Core;

internal static class BatchFeatures
{
    public static void Run(string root,CertificateIdentity identity,Action<bool,string> check,Action<Action,string> refuses)
    {
        string folder=Path.Combine(root,"batch-fixtures");Directory.CreateDirectory(folder);
        string first=Path.Combine(folder,"Contrato á.PDF"),second=Path.Combine(folder,"Informe.pdf");
        File.Copy(Path.Combine(root,"original-0.pdf"),first,true);File.Copy(Path.Combine(root,"original-90.pdf"),second,true);
        byte[] before1=File.ReadAllBytes(first),before2=File.ReadAllBytes(second);
        var a=new SigningJob(first,BatchSigner.OutputPath(first),new(0,.15,.55,.5,.15),new("Firma A\n{nombre}",null,false,6));
        var b=new SigningJob(second,BatchSigner.OutputPath(second),new(0,.15,.55,.5,.15),new("Firma B\n{fecha}",CustomFeatures.Image(),false,7));
        foreach(var job in new[]{a,b})if(File.Exists(job.Destination))File.Delete(job.Destination);
        check(a.Destination==Path.Combine(folder,"Contrato á_firmado.pdf"),"El sufijo de la copia no es _firmado.");
        File.WriteAllText(a.Destination,"Existing copy, must be preserved");
        refuses(()=>BatchSigner.Sign([a,b],identity),"Se inicia un lote con un destino existente.");
        check(File.ReadAllText(a.Destination)=="Existing copy, must be preserved"&&!File.Exists(b.Destination),"Se sobrescribe una copia o se guarda parcialmente un lote con colisión.");
        File.Delete(a.Destination);
        refuses(()=>BatchSigner.CheckDestinations([a,b with{Destination=a.Destination}]),"Se aceptan destinos duplicados.");
        refuses(()=>BatchSigner.CheckDestinations([a with{Destination=first}]),"Se permite sobrescribir un original.");
        using(var authority=new TestTimestampAuthority())
        {
            int calls=0;
            var tsa=new TimestampClient(bytes=>{if(++calls==2)throw new IOException("Fallo de TSA en el segundo documento");return authority.Respond(bytes);},authority.Root);
            refuses(()=>BatchSigner.Sign([a,b],identity,tsa),"No falla el lote cuando falla el sello solicitado.");
            check(!File.Exists(a.Destination)&&!File.Exists(b.Destination),"Un error en el segundo documento publica parcialmente el lote.");
            check(!Directory.EnumerateFiles(folder,".firmapdf-lote-*").Any(),"Se dejan copias temporales tras un error del lote.");
        }
        var progress=new List<int>();var results=BatchSigner.Sign([a,b],identity,null,(current,total)=>{if(total==2)progress.Add(current);});
        check(results.Count==2&&progress.SequenceEqual(new[]{1,2}),"No se firman todos los PDF secuencialmente.");
        check(PdfEngine.Verify(a.Destination).LatestCoversWholeFile&&PdfEngine.Verify(b.Destination).LatestCoversWholeFile,"Una copia del lote no supera la verificación.");
        check(before1.SequenceEqual(File.ReadAllBytes(first))&&before2.SequenceEqual(File.ReadAllBytes(second)),"El lote modifica los originales.");
        check(!Directory.EnumerateFiles(folder,".firmapdf-lote-*").Any(),"No se limpian las copias temporales del lote.");
        Console.WriteLine("Firma por lotes, colisiones, fallo de TSA y originales intactos: correctos.");
    }
}
