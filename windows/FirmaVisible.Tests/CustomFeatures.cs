using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FirmaVisible.App;
using FirmaVisible.Core;
using iText.Kernel.Pdf;
using iText.Signatures;

internal static class CustomFeatures
{
    public static byte[] ExampleImage()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var path = new StreamGeometry();
            using (var line = path.Open())
            {
                line.BeginFigure(new Point(20, 62), false, false);
                line.BezierTo(new Point(100, -10), new Point(55, 100), new Point(72, 42), true, false);
                line.BezierTo(new Point(95, 10), new Point(78, 85), new Point(109, 48), true, false);
                line.BezierTo(new Point(145, 10), new Point(110, 83), new Point(155, 38), true, false);
                line.BezierTo(new Point(190, 11), new Point(140, 95), new Point(214, 48), true, false);
            }
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(40, 72, 168)), 3);
            drawing.DrawGeometry(null, pen, path); drawing.DrawLine(pen, new Point(23, 78), new Point(220, 65));
        }
        var bitmap = new RenderTargetBitmap(240, 90, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    public static byte[] Image(int width = 180, int height = 60, byte alpha = 255)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i + 2] = 255; pixels[i + 3] = alpha; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
    private static byte[] Pixel(BitmapSource source, int x, int y)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); byte[] pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0); return pixel;
    }
    public static async Task RunAsync(string root, CertificateIdentity identity, Action<bool, string> check, Action<Action, string> refuses)
    {
        using var authority = new TestTimestampAuthority();
        var design = new SignatureDesign("Revisado por {nombre}\nÁrea técnica · Málaga\n{fecha}", Image(), true);
        foreach (int rotation in new[] { 0, 90, 180, 270 })
        {
            string source = Path.Combine(root, $"original-{rotation}.pdf"), output = Path.Combine(root, $"custom-{rotation}.pdf");
            if (File.Exists(output)) File.Delete(output);
            byte[] before = File.ReadAllBytes(source);
            PdfEngine.Sign(source, output, new(0, .15, .55, .5, .15), identity, design, authority.Client());
            var verification = PdfEngine.Verify(output);
            check(verification.TimestampCount == 1 && verification.LatestTimestamp is not null, "Sello sintético no verificable.");
            check(verification.LatestCoversWholeFile && before.SequenceEqual(File.ReadAllBytes(source)), "La apariencia personalizada modifica el original.");
            var page = await PdfPreview.RenderAsync(PdfEngine.PreparePreview(output, root), 0);
            byte[] pixel = Pixel(page.Image, (int)(page.Image.PixelWidth * .23), (int)(page.Image.PixelHeight * .625));
            Save(page.Image, Path.Combine(root, $"custom-{rotation}.png"));
            check(pixel[2] > 220 && pixel[1] < 50 && pixel[0] < 50, $"Imagen personalizada fuera de posición: {rotation}.");
        }
        string original = Path.Combine(root, "original-0.pdf"), imageOnly = Path.Combine(root, "image-only.pdf");
        if (File.Exists(imageOnly)) File.Delete(imageOnly);
        PdfEngine.Sign(original, imageOnly, new(0, .15, .55, .5, .15), identity, new SignatureDesign("", Image(), false));
        check(PdfEngine.Verify(imageOnly).TimestampCount == 0, "Se añadió un sello sin solicitarlo.");
        var onlyImage = await PdfPreview.RenderAsync(PdfEngine.PreparePreview(imageOnly, root), 0);
        byte[] clear = Pixel(onlyImage.Image, (int)(onlyImage.Image.PixelWidth * .16), (int)(onlyImage.Image.PixelHeight * .56));
        check(clear[0] > 245 && clear[1] > 245 && clear[2] > 245, "El modo transparente añade un fondo.");
        string rejected = Path.Combine(root, $"rejected-custom-{Guid.NewGuid():N}.pdf");
        refuses(() => PdfEngine.Sign(original, rejected, new(0, .1, .5, .5, .1), identity, design, new TimestampClient(_ => throw new IOException("Sin conexión de prueba"), authority.Root)), "Se produjo una firma sin el sello solicitado.");
        check(!File.Exists(rejected), "Se dejó un resultado de sellado fallido.");
        refuses(() => PdfEngine.Sign(original, rejected, new(0, .1, .5, .18, .06), identity, new SignatureDesign(string.Concat(Enumerable.Repeat("Texto largo ", 49)), null, true)), "Se truncó silenciosamente un texto ilegible.");
        refuses(() => _ = new SignatureDesign("", null, false), "Se aceptó una apariencia vacía.");
        byte[] imprint = SHA256.HashData(new byte[] { 1, 2, 3 });
        refuses(() => new TimestampClient(bytes => authority.Respond(bytes, wrongNonce: true), authority.Root).GetTimeStampToken(imprint), "Se aceptó un nonce ajeno.");
        refuses(() => new TimestampClient(bytes => authority.Respond(bytes, wrongHash: true), authority.Root).GetTimeStampToken(imprint), "Se aceptó otra huella.");
        using var other = new TestTimestampAuthority();
        refuses(() => new TimestampClient(bytes => authority.Respond(bytes), other.Root).GetTimeStampToken(imprint), "Se aceptó otra autoridad de sellado.");
        CorruptTimestamp(Path.Combine(root, "custom-0.pdf"), Path.Combine(root, "bad-timestamp.pdf"));
        refuses(() => PdfEngine.Verify(Path.Combine(root, "bad-timestamp.pdf")), "No se detectó un sello alterado.");

        var store = new UserPreferencesStore(Path.Combine(root, "preferences-test"));
        store.Save(new(design, false)); var reloaded = new UserPreferencesStore(Path.GetDirectoryName(store.FilePath)!).Load();
        check(!reloaded.UseTimestamp && reloaded.Design.Template == design.Template && reloaded.Design.ImagePng!.SequenceEqual(design.ImagePng!), "El diseño no persiste entre sesiones.");
        check(!File.ReadAllText(store.FilePath).Contains("PRIVATE KEY") && !File.ReadAllText(store.FilePath).Contains("Certificate"), "Las preferencias contienen datos de certificado.");
        store.Save(new(new SignatureDesign(SignatureDesign.DefaultText, null, false), true));
        check(store.Load().Design.ImagePng is null && store.Load().UseTimestamp, "No se conserva la retirada de imagen o el sello.");
        File.WriteAllText(store.FilePath, "invalid-json"); refuses(() => store.Load(), "Se ignoraron preferencias corruptas sin informar.");
        File.Delete(store.FilePath);
        byte[] normalized = ImageNormalizer.Normalize(Image(4096, 2048, 120));
        var decoded = ImageNormalizer.Decode(normalized);
        check(decoded.PixelWidth <= 1200 && decoded.PixelHeight <= 1200, "No se limita la imagen importada.");
        check(Pixel(decoded, 0, 0)[3] < 255, "Se pierde la transparencia de la imagen.");
        refuses(() => ImageNormalizer.Normalize(new byte[] { 1, 2, 3 }), "Se aceptó una imagen no válida.");
        if (Environment.GetEnvironmentVariable("FIRMAVISIBLE_LIVE_TSA") == "true")
        {
            string output = Path.Combine(root, "real-timestamp.pdf"); if (File.Exists(output)) File.Delete(output);
            PdfEngine.Sign(original, output, new(0, .15, .55, .5, .15), identity, design, TimestampClient.DigiCert());
            check(PdfEngine.Verify(output).TimestampCount == 1, "Sello real no verificable.");
            ExportTimestamp(output, root);
            Console.WriteLine("Sello real DigiCert: nonce, firma, enlace y cadena .NET correctos.");
        }
        Console.WriteLine("Diseño, imagen, preferencias y sellos de tiempo: correctos.");
    }
    private static (byte[] Cms, byte[] Pdf, int Start) ReadCms(string path)
    {
        using var doc = new PdfDocument(new PdfReader(path));
        var util = new SignatureUtil(doc); var dictionary = util.GetSignatureDictionary(util.GetSignatureNames()[0]);
        return (dictionary.GetAsString(PdfName.Contents).GetValueBytes(), File.ReadAllBytes(path), dictionary.GetAsArray(PdfName.ByteRange).GetAsNumber(1).IntValue() + 1);
    }
    private static void CorruptTimestamp(string source, string output)
    {
        var data = ReadCms(source); var cms = new SignedCms(); cms.Decode(data.Cms);
        var attribute = cms.SignerInfos[0].UnsignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid.Value == TimestampClient.TimestampOid);
        Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _);
        byte[] signature = token!.AsSignedCms().SignerInfos[0].GetSignature(); int index = data.Cms.AsSpan().IndexOf(signature);
        if (index < 0) throw new Exception("No se encontró el sello para la prueba."); data.Cms[index + signature.Length - 1] ^= 1;
        Encoding.ASCII.GetBytes(Convert.ToHexString(data.Cms)).CopyTo(data.Pdf, data.Start); File.WriteAllBytes(output, data.Pdf);
    }
    private static void ExportTimestamp(string source, string root)
    {
        var data = ReadCms(source); var cms = new SignedCms(); cms.Decode(data.Cms); var signer = cms.SignerInfos[0];
        var timestamp = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single(a => a.Oid.Value == TimestampClient.TimestampOid).Values[0].RawData;
        Rfc3161TimestampToken.TryDecode(timestamp, out var token, out _);
        File.WriteAllBytes(Path.Combine(root, "timestamp.der"), timestamp); File.WriteAllBytes(Path.Combine(root, "signature-value.bin"), signer.GetSignature());
        File.WriteAllText(Path.Combine(root, "tsa-chain.pem"), string.Join("\n", token!.AsSignedCms().Certificates.Cast<X509Certificate2>().Select(c => c.ExportCertificatePem())));
    }
    private static void Save(BitmapSource image, string path)
    { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
}
