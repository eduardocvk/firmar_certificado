using System.Formats.Asn1;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using FirmaVisible.Core;

internal static class MoreFormatsFeatures
{
    public static void Run(string root,CertificateIdentity identity,Action<bool,string> check,Action<Action,string> refuses)
    {
        string folder=Path.Combine(root,"other-files");Directory.CreateDirectory(folder);
        string original=Path.Combine(folder,"Documento á.xml"),signature=original+".p7s";
        File.WriteAllText(original,"<?xml version=\"1.0\"?><documento>Prueba sin datos personales</documento>");byte[] before=File.ReadAllBytes(original);
        DetachedSignature.Sign(original,signature,identity);
        File.WriteAllText(Path.Combine(folder,"test-signer.pem"),identity.Certificate.ExportCertificatePem());
        var cms=DetachedSignature.ReadVerified(original,signature);
        check(cms.Detached&&cms.SignerInfos.Count==1&&cms.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().Any(a=>a.Oid.Value==DetachedSignature.SigningCertificateV2Oid),"No se crea una firma CAdES separada con referencia al certificado.");
        check(DetachedSignature.Verify(original,signature).LatestCoversWholeFile&&File.ReadAllBytes(original).SequenceEqual(before),"La firma separada cambia el original o no supera la verificación.");
        refuses(()=>DetachedSignature.Sign(original,signature,identity),"Se sobrescribe una firma existente.");
        string wrong=Path.Combine(folder,"otro.xml");File.WriteAllText(wrong,"<otro/>");
        refuses(()=>DetachedSignature.Verify(wrong,signature),"Se acepta otro archivo como original de una firma separada.");
        string altered=Path.Combine(folder,"alterada.p7s");byte[] bytes=File.ReadAllBytes(signature);bytes[^1]^=1;File.WriteAllBytes(altered,bytes);
        refuses(()=>DetachedSignature.Verify(original,altered),"Se acepta una firma CMS alterada.");
        var wrongBinding=new SignedCms(new ContentInfo(before),true);var signer=new CmsSigner(identity.Certificate){IncludeOption=X509IncludeOption.EndCertOnly,DigestAlgorithm=new Oid("2.16.840.1.101.3.4.2.1")};
        var writer=new AsnWriter(AsnEncodingRules.DER);using(writer.PushSequence())using(writer.PushSequence())using(writer.PushSequence())writer.WriteOctetString(new byte[32]);
        signer.SignedAttributes.Add(new AsnEncodedData(new Oid(DetachedSignature.SigningCertificateV2Oid),writer.Encode()));wrongBinding.ComputeSignature(signer);File.WriteAllBytes(altered,wrongBinding.Encode());
        refuses(()=>DetachedSignature.Verify(original,altered),"Se acepta una referencia CAdES a otro certificado aunque el CMS sea íntegro.");
        using(var ecKey=ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var request=new CertificateRequest("CN=EC CMS TEST - NO REAL",ecKey,HashAlgorithmName.SHA256);
            using var ec=new CertificateIdentity(request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1),DateTimeOffset.Now.AddDays(1)));
            DetachedSignature.Sign(original,Path.Combine(folder,"ec.p7s"),ec);check(DetachedSignature.Verify(original,Path.Combine(folder,"ec.p7s")).Count==1,"La firma separada EC no es válida.");
        }
        using(var tsa=new TestTimestampAuthority())
        {
            string stamped=Path.Combine(folder,"sellada.p7s");DetachedSignature.Sign(original,stamped,identity,tsa.Client());
            check(DetachedSignature.Verify(original,stamped).TimestampCount==1,"La firma separada pierde el sello de tiempo.");
            var report=SignatureInspector.InspectDetached(original,stamped,online:false);
            check(report.Certificates.Count==2&&report.Certificates.All(c=>c.Check.Revocation==RevocationResult.NotChecked),"El informe no distingue firmante y TSA o inventa una comprobación de revocación.");
            string failed=Path.Combine(folder,"sin-sello.p7s");
            refuses(()=>DetachedSignature.Sign(original,failed,identity,new TimestampClient(_=>throw new IOException("TSA de prueba caída"),tsa.Root)),"Se crea una firma degradada sin el sello solicitado.");
            check(!File.Exists(failed),"Se publica una firma cuando falla su sello.");
        }
        string pdf=Path.Combine(folder,"Informe.pdf");File.Copy(Path.Combine(root,"original-0.pdf"),pdf);
        string text=Path.Combine(folder,"Notas.txt");File.WriteAllText(text,"Documento de prueba");
        var jobs=new[]{new SigningJob(pdf,BatchSigner.OutputPath(pdf),new(0,.1,.5,.4,.1),SignatureDesign.Standard()),new SigningJob(text,BatchSigner.OutputPath(text),null,SignatureDesign.Standard())};
        var results=BatchSigner.Sign(jobs,identity);
        check(results.Count==2&&PdfEngine.Verify(jobs[0].Destination).Count==1&&DetachedSignature.Verify(text,jobs[1].Destination).Count==1,"No se firma y verifica un lote mixto PDF y texto.");
        var pdfReport=SignatureInspector.InspectPdf(jobs[0].Destination,online:false);
        check(pdfReport.Integrity.Count==1&&pdfReport.Certificates.Count==1&&!pdfReport.Certificates[0].Check.Trusted,"Se acepta como confiable en Windows un certificado sintético sin instalar.");
        string oversized=Path.Combine(folder,"grande.bin");using(var stream=File.Create(oversized))stream.SetLength(DetachedSignature.MaximumFileSize+1);
        refuses(()=>DetachedSignature.Sign(oversized,oversized+".p7s",identity),"No se controla el tamaño de la firma CMS en memoria.");File.Delete(oversized);
        foreach(var mode in new[]{"good","revoked","unknown"})
        {
            using var authority=new RevocationAuthority(mode);
            var roots=new X509Certificate2Collection(authority.Root);var extra=new X509Certificate2Collection(authority.Root);
            var report=CertificateValidation.CheckCore(authority.Leaf,extra,true,roots);
            var expected=mode=="good"?RevocationResult.Good:mode=="revoked"?RevocationResult.Revoked:RevocationResult.Unknown;
            check(report.Trusted&&report.InDate&&report.Revocation==expected,$"Revocación {mode}: {report}");
            check(!CertificateValidation.Check(authority.Leaf,extra,false).Trusted,"La raíz de prueba ha modificado la confianza de Windows.");
        }
        Console.WriteLine("CMS/CAdES separada, EC, lotes mixtos y revocación buena/revocada/desconocida: correctos.");
    }
    private sealed class RevocationAuthority:IDisposable
    {
        private readonly TcpListener listener=new(IPAddress.Loopback,0);
        private readonly CancellationTokenSource stop=new();
        private readonly Task server;
        private readonly byte[] crl;
        private readonly bool unavailable;
        public X509Certificate2 Root{get;}
        public X509Certificate2 Leaf{get;}
        public RevocationAuthority(string mode)
        {
            unavailable=mode=="unknown";listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            using var rootKey=RSA.Create(2048);var rootRequest=new CertificateRequest("CN=CRL TEST "+Guid.NewGuid().ToString("N"),rootKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign|X509KeyUsageFlags.CrlSign,true));rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey,false));
            Root=rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2),DateTimeOffset.UtcNow.AddDays(2));
            using var leafKey=RSA.Create(2048);var request=new CertificateRequest("CN=Revocation TEST - NO REAL",leafKey,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
            var distribution=new AsnWriter(AsnEncodingRules.DER);using(distribution.PushSequence())using(distribution.PushSequence())using(distribution.PushSequence(new Asn1Tag(TagClass.ContextSpecific,0,true)))using(distribution.PushSequence(new Asn1Tag(TagClass.ContextSpecific,0,true)))distribution.WriteCharacterString(UniversalTagNumber.IA5String,$"http://127.0.0.1:{port}/{Guid.NewGuid():N}.crl",new Asn1Tag(TagClass.ContextSpecific,6));
            request.CertificateExtensions.Add(new X509Extension("2.5.29.31",distribution.Encode(),false));
            Leaf=request.Create(Root,DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1),RandomNumberGenerator.GetBytes(16));
            var builder=new CertificateRevocationListBuilder();if(mode=="revoked")builder.AddEntry(Leaf,DateTimeOffset.UtcNow.AddMinutes(-5),X509RevocationReason.KeyCompromise);
            crl=builder.Build(Root,BigInteger.One,DateTimeOffset.UtcNow.AddHours(1),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1,DateTimeOffset.UtcNow.AddMinutes(-1));
            server=ServeAsync();
        }
        private async Task ServeAsync()
        {
            while(!stop.IsCancellationRequested)
            {
                try
                {
                    using var client=await listener.AcceptTcpClientAsync(stop.Token);using var stream=client.GetStream();
                    byte[] buffer=new byte[8192];int used=0;
                    while(used<buffer.Length){int count=await stream.ReadAsync(buffer.AsMemory(used),stop.Token);if(count==0)break;used+=count;if(System.Text.Encoding.ASCII.GetString(buffer,0,used).Contains("\r\n\r\n"))break;}
                    byte[] header=System.Text.Encoding.ASCII.GetBytes(unavailable?"HTTP/1.1 503 Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n":$"HTTP/1.1 200 OK\r\nContent-Type: application/pkix-crl\r\nContent-Length: {crl.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header,stop.Token);if(!unavailable)await stream.WriteAsync(crl,stop.Token);
                }
                catch(OperationCanceledException){break;}catch(SocketException)when(stop.IsCancellationRequested){break;}catch(IOException){ }
            }
        }
        public void Dispose(){stop.Cancel();listener.Stop();server.GetAwaiter().GetResult();stop.Dispose();Leaf.Dispose();Root.Dispose();}
    }
}
