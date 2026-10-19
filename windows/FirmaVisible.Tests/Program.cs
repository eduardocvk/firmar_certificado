using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FirmaVisible.Core;
using FirmaVisible.App;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Signatures;
using PdfDoc = iText.Kernel.Pdf.PdfDocument;
using Path = System.IO.Path;
using PdfRectangle = iText.Kernel.Geom.Rectangle;
using WpfRectangle = System.Windows.Rect;

internal static class Program
{
    private static string root = null!;
    private static int assertions;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            root = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/windows-tests"); Directory.CreateDirectory(root);
            RunAsync().GetAwaiter().GetResult();
            RenderInterface();
            Console.WriteLine($"CORRECTO: {assertions} comprobaciones, firma .NET, CMS independiente y vista nativa.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string message)
    {
        assertions++; if (!condition) throw new Exception(message);
    }
    private static void Refuses(Action action, string message)
    {
        try { action(); } catch { assertions++; return; } throw new Exception(message);
    }
    private static CertificateIdentity Identity(bool ec = false, bool expired = false)
    {
        var begin = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddHours(expired ? -1 : 24);
        if (ec)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=FirmaVisible EC TEST - NO REAL", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            return new(request.CreateSelfSigned(begin, end));
        }
        else
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=Persona de prueba - NO REAL,O=FirmaVisible Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            return new(request.CreateSelfSigned(begin, end));
        }
    }
    private static string Original(int rotation)
    {
        string path = Path.Combine(root, $"original-{rotation}.pdf");
        using var doc = new PdfDoc(new PdfWriter(path));
        var page = doc.AddNewPage(new PageSize(650, 850));
        page.SetCropBox(new PdfRectangle(20, 30, 600, 780)); page.SetRotation(rotation);
        var canvas = new PdfCanvas(page);
        canvas.BeginText().SetFontAndSize(PdfFontFactory.CreateFont(iText.IO.Font.Constants.StandardFonts.HELVETICA), 20)
            .MoveText(50, 750).ShowText("Documento de prueba C#").EndText();
        doc.AddNewPage(PageSize.A4);
        return path;
    }

    private static async Task RunAsync()
    {
        using var rsa = Identity(); using var ec = Identity(true);
        foreach (int rotation in new[] {0,90,180,270})
        {
            string input = Original(rotation), output = Path.Combine(root, $"signed-{rotation}.pdf");
            // Every test invocation uses a fresh destination; preserve previous results safely.
            if (File.Exists(output)) File.Delete(output);
            byte[] original = File.ReadAllBytes(input);
            PdfEngine.Sign(input, output, new(0,.15,.55,.5,.15), rsa);
            Check(original.SequenceEqual(File.ReadAllBytes(input)), "El original ha cambiado.");
            var verified = PdfEngine.Verify(output);
            Check(verified.Count == 1 && verified.LatestCoversWholeFile, "Firma RSA o cobertura incorrecta.");
            using (var doc = new PdfDoc(new PdfReader(output)))
            {
                var util = new SignatureUtil(doc); string name = util.GetSignatureNames()[0];
                var dict = util.GetSignatureDictionary(name);
                Check(dict.GetAsName(PdfName.SubFilter).Equals(PdfName.ETSI_CAdES_DETACHED), "SubFilter incorrecto.");
                var ranges = dict.GetAsArray(PdfName.ByteRange).ToIntArray(); byte[] pdf = File.ReadAllBytes(output);
                byte[] content = new byte[ranges[1]+ranges[3]];
                Array.Copy(pdf,0,content,0,ranges[1]); Array.Copy(pdf,ranges[2],content,ranges[1],ranges[3]);
                byte[] cms = dict.GetAsString(PdfName.Contents).GetValueBytes();
                var independent = new SignedCms(new ContentInfo(content), true); independent.Decode(cms); independent.CheckSignature(true);
                Check(independent.SignerInfos.Count == 1, "CMS no verificable con el proveedor de Windows.");
                Check(independent.SignerInfos[0].SignedAttributes.Cast<CryptographicAttributeObject>().Any(a => a.Oid.Value == "1.2.840.113549.1.9.16.2.47"), "Falta SigningCertificateV2.");
                File.WriteAllBytes(Path.Combine(root,$"signature-{rotation}.der"), cms);
                File.WriteAllBytes(Path.Combine(root,$"content-{rotation}.bin"), content);
                File.WriteAllBytes(Path.Combine(root,$"tampered-{rotation}.bin"), content.Select((b,i) => i==0 ? (byte)(b^1) : b).ToArray());
                Check(doc.GetPage(1).GetAnnotations().Count == 1, "La anotación de firma no está registrada en la página.");
            }
            byte[] signed = File.ReadAllBytes(output);
            string display = PdfEngine.PreparePreview(output, root);
            Check(signed.SequenceEqual(File.ReadAllBytes(output)), "La vista previa ha modificado el PDF firmado.");
            var pageImage = await PdfPreview.RenderAsync(display, 0);
            var rgb = new FormatConvertedBitmap(pageImage.Image, PixelFormats.Bgra32, null, 0);
            int x = (int)(rgb.PixelWidth*.17), y = (int)(rgb.PixelHeight*.68);
            byte[] pixel = new byte[4]; rgb.CopyPixels(new Int32Rect(x,y,1,1), pixel,4,0);
            SaveImage(rgb, Path.Combine(root,$"rotation-{rotation}.png"));
            int ink = 0; byte[] pixels = new byte[rgb.PixelWidth * rgb.PixelHeight * 4]; rgb.CopyPixels(pixels, rgb.PixelWidth * 4, 0);
            for (int py=(int)(rgb.PixelHeight*.55); py<(int)(rgb.PixelHeight*.7); py++) for(int px=(int)(rgb.PixelWidth*.15); px<(int)(rgb.PixelWidth*.65); px++) { int index=(py*rgb.PixelWidth+px)*4; if(pixels[index+2]<160 && pixels[index+1]<160) ink++; }
            Check(ink > 50, $"Texto de firma fuera de sitio ({rotation}).");
            Console.WriteLine($"Rotación {rotation}: firma RSA, CMS y apariencia correctos.");
        }

        string first = Path.Combine(root,"ec.pdf"), second = Path.Combine(root,"second.pdf");
        if (File.Exists(first)) File.Delete(first); if (File.Exists(second)) File.Delete(second);
        PdfEngine.Sign(Path.Combine(root,"original-0.pdf"), first, new(0,.1,.6,.5,.1), ec);
        byte[] firstBytes = File.ReadAllBytes(first);
        PdfEngine.Sign(first, second, new(1,.2,.5,.5,.12), rsa);
        Check(firstBytes.SequenceEqual(File.ReadAllBytes(second).Take(firstBytes.Length)), "La segunda firma no conserva la revisión anterior.");
        Check(PdfEngine.Verify(second).Count == 2, "La segunda firma ha invalidado la primera.");
        Console.WriteLine("EC y segunda firma incremental: correctos.");

        string originalPath = Path.Combine(root,"original-0.pdf");
        byte[] originalBytes = File.ReadAllBytes(originalPath);
        Refuses(() => PdfEngine.Sign(originalPath,originalPath,new(0,.1,.6,.5,.1),rsa), "Se permitió sobrescribir el original.");
        Check(originalBytes.SequenceEqual(File.ReadAllBytes(originalPath)), "Se modificó el original rechazado.");
        Refuses(() => PdfEngine.Sign(originalPath,second,new(0,.1,.6,.5,.1),rsa), "Se permitió sobrescribir el destino.");
        using var expired = Identity(expired: true);
        string rejected = Path.Combine(root, $"rejected-{Guid.NewGuid():N}.pdf");
        Refuses(() => PdfEngine.Sign(originalPath,rejected,new(0,.1,.6,.5,.1),expired), "Se permitió un certificado caducado.");
        Refuses(() => PdfEngine.Sign(originalPath,rejected,new(0,.1,.6,.01,.01),rsa), "Se permitió un recuadro ilegible.");
        Check(!File.Exists(rejected), "Se dejó un archivo de firma rechazado.");
        Refuses(() => _ = new Placement(0,.95,.1,.2,.1), "Se permitió un recuadro fuera de página.");

        string pfx = Path.Combine(root,"test-only.pfx");
        File.WriteAllBytes(pfx,rsa.Certificate.Export(X509ContentType.Pfx,"test-only"));
        var imported = CertificateIdentity.FromPkcs12(pfx,"test-only".AsSpan());
        Check(imported.Count == 1 && imported[0].Name == rsa.Name,"No se importó la identidad PKCS#12.");
        foreach(var value in imported)value.Dispose();
        Refuses(() => CertificateIdentity.FromPkcs12(pfx,"wrong".AsSpan()),"Se aceptó una contraseña incorrecta.");
        File.Delete(pfx);

        string altered = Path.Combine(root,"altered.pdf"); File.Copy(first,altered,true);
        byte[] changed = File.ReadAllBytes(altered);
        byte[] marker = System.Text.Encoding.ASCII.GetBytes("TEST");
        int offset = changed.AsSpan().IndexOf(marker);
        if(offset < 0) offset = changed.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes("FirmaPDF"));
        Check(offset > 0,"No se encontró contenido para la prueba de alteración."); changed[offset] ^= 1; File.WriteAllBytes(altered,changed);
        Refuses(() => PdfEngine.Verify(altered), "No se detectó una alteración.");

        string appended = Path.Combine(root,"appended.pdf"); File.Copy(first,appended,true); File.AppendAllText(appended,"\n% later change\n");
        Check(!PdfEngine.Verify(appended).LatestCoversWholeFile,"No se informó de bytes posteriores a la firma.");
        Refuses(() => PdfEngine.Sign(appended,rejected,new(0,.1,.6,.5,.1),rsa),"Se firmó una revisión con cambios posteriores.");

        string locked = Path.Combine(root,"certified.pdf");
        using(var doc = new PdfDoc(new PdfReader(originalPath),new PdfWriter(locked)))
        {
            var perms = new PdfDictionary(); perms.Put(PdfName.DocMDP,new PdfDictionary()); doc.GetCatalog().GetPdfObject().Put(PdfName.Perms,perms);
        }
        Refuses(() => PdfEngine.Sign(locked,rejected,new(0,.1,.6,.5,.1),rsa),"Se modificó un documento certificado.");
        await CustomFeatures.RunAsync(root, rsa, Check, Refuses);
    }

    private static void SaveImage(BitmapSource image,string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
    private static void RenderInterface()
    {
        var app = new FirmaVisible.App.App(startWindow: false); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow(new UserPreferencesStore(Path.Combine(root,"ui-preferences")));
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1220,800)); content.Arrange(new WpfRectangle(0,0,1220,800)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1220,800,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
        SaveImage(bitmap,Path.Combine(root,"windows-interface.png")); window.Close();
        Check(true,"Interfaz WPF renderizada.");
        var editor = new AppearanceEditor(new SignatureDesign("Firmado por {nombre}\n{fecha}",CustomFeatures.ExampleImage(),true), "Tu nombre y apellidos", "Emisor del certificado");
        var editorContent = (FrameworkElement)editor.Content;
        editorContent.Measure(new Size(724,700)); editorContent.Arrange(new WpfRectangle(0,0,724,700)); editorContent.UpdateLayout();
        var editorBitmap = new RenderTargetBitmap(724,700,96,96,PixelFormats.Pbgra32); editorBitmap.Render(editorContent);
        SaveImage(editorBitmap,Path.Combine(root,"windows-appearance-editor.png")); editor.Close();
        Check(true,"Editor WPF renderizado.");
        ExplorerFeatures.Run(root, Check, Refuses);
    }
}
