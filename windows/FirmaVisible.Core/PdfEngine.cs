using iText.Bouncycastle.X509;
using iText.Commons.Bouncycastle.Cert;
using iText.Forms;
using iText.Forms.Form.Element;
using iText.Kernel.Colors;
using iText.Kernel.Pdf;
using iText.Layout.Borders;
using iText.Signatures;
using Org.BouncyCastle.X509;

namespace FirmaVisible.Core;

public sealed record Verification(int Count, bool LatestCoversWholeFile)
{
    public override string ToString() => Count == 0 ? "El PDF no contiene firmas digitales." :
        $"{Count} firma(s).\n{PdfEngine.VerificationNotice}" +
        (LatestCoversWholeFile ? "" : "\nHay cambios posteriores a la última firma.");
}

public static class PdfEngine
{
    public const string VerificationNotice = "Integridad criptográfica comprobada. Confianza, revocación y cualificación no comprobadas. Sin sello de tiempo.";

    public static Verification Verify(string path)
    {
        using var reader = new PdfReader(path);
        using var doc = new PdfDocument(reader);
        var util = new SignatureUtil(doc);
        var names = util.GetSignatureNames();
        foreach (string name in names)
        {
            var pkcs7 = util.ReadSignatureData(name);
            if (!pkcs7.VerifySignatureIntegrityAndAuthenticity())
                throw new InvalidDataException("La integridad de una firma del PDF no es correcta.");
        }
        return new(names.Count, names.Count > 0 && util.SignatureCoversWholeDocument(names[^1]));
    }

    public static void Sign(string input, string output, Placement placement, CertificateIdentity identity)
    {
        identity.CheckUsable();
        if (StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(input), Path.GetFullPath(output)))
            throw new IOException("Guarda una copia; no sobrescribas el original.");
        if (File.Exists(output)) throw new IOException("El destino ya existe. Elige otro nombre.");
        using (var reader = new PdfReader(input))
        using (var doc = new PdfDocument(reader))
        {
            CheckDocument(doc);
            if (placement.Page >= doc.GetNumberOfPages()) throw new ArgumentException("La página no existe.");
        }
        var previous = Verify(input);
        if (previous.Count > 0 && !previous.LatestCoversWholeFile)
            throw new InvalidDataException("Hay cambios posteriores a la última firma. Revisa el documento antes de añadir otra.");
        string folder = Path.GetDirectoryName(Path.GetFullPath(output))!;
        string temp = Path.Combine(folder, $".firma-{Guid.NewGuid():N}.pdf");
        try
        {
            using (var reader = new PdfReader(input))
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var signer = new PdfSigner(reader, stream, new StampingProperties().UseAppendMode());
                var doc = signer.GetDocument();
                var page = doc.GetPage(placement.Page + 1);
                var crop = page.GetCropBox();
                bool rotated = page.GetRotation() % 180 != 0;
                double visualWidth = placement.Width * (rotated ? crop.GetHeight() : crop.GetWidth());
                double visualHeight = placement.Height * (rotated ? crop.GetWidth() : crop.GetHeight());
                if (visualWidth < 100 || visualHeight < 42)
                    throw new ArgumentException("El recuadro debe medir al menos 100 × 42 puntos PDF. Amplíalo para que la firma sea legible.");
                string fieldName = $"FirmaVisible_{Guid.NewGuid():N}";
                DateTime now = DateTime.Now;
                var appearance = new SignatureFieldAppearance(fieldName)
                    .SetContent($"Firmado digitalmente\n{identity.Name}\n{now:dd/MM/yyyy HH:mm zzz}")
                    .SetFontSize((float)Math.Min(9, (visualHeight - 8) / 4.4))
                    .SetBackgroundColor(new DeviceRgb(242, 247, 255))
                    .SetFontColor(new DeviceRgb(22, 48, 79))
                    .SetBorder(new SolidBorder(new DeviceRgb(40, 83, 150), .7f));
                signer.SetSignerProperties(new SignerProperties()
                    .SetFieldName(fieldName).SetPageNumber(placement.Page + 1)
                    .SetPageRect(placement.ToPdf(crop, page.GetRotation()))
                    .SetClaimedSignDate(now).SetSignatureCreator("FirmaVisible .NET")
                    .SetSignatureAppearance(appearance));
                doc.GetDocumentInfo().SetMoreInfo("License", "iText Community, Copyright Apryse Group NV; AGPL-3.0-or-later. Source: https://github.com/eduardocvk/firmar_certificado");
                var parser = new X509CertificateParser();
                IX509Certificate[] chain = identity.GetPublicChain()
                    .Select(c => (IX509Certificate)new X509CertificateBC(parser.ReadCertificate(c))).ToArray();
                signer.SignDetached(new WindowsKeySignature(identity), chain, null, null, null, 32768, PdfSigner.CryptoStandard.CADES);
            }
            var result = Verify(temp);
            if (result.Count != previous.Count + 1 || !result.LatestCoversWholeFile)
                throw new InvalidDataException("La firma generada no supera la comprobación de integridad.");
            File.Move(temp, output, overwrite: false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static void CheckDocument(PdfDocument doc)
    {
        if (doc.GetReader().IsEncrypted()) throw new InvalidDataException("Esta versión no firma PDF cifrados.");
        if (doc.GetNumberOfPages() == 0) throw new InvalidDataException("El PDF no tiene páginas.");
        if (doc.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Perms)?.ContainsKey(PdfName.DocMDP) == true)
            throw new InvalidDataException("El PDF está certificado y tiene restricciones de modificación.");
        var form = PdfAcroForm.GetAcroForm(doc, false);
        if (form is null) return;
        if (form.GetPdfObject().ContainsKey(PdfName.XFA)) throw new InvalidDataException("Los formularios XFA no son compatibles con esta versión.");
        if (form.GetAllFormFields().Values.Any(f => f.GetPdfObject().ContainsKey(PdfName.Lock)))
            throw new InvalidDataException("El PDF contiene campos con bloqueo de firma.");
    }

    /// <summary>Disposable rendering copy only; never sign or export this copy.</summary>
    public static string PreparePreview(string original, string directory)
    {
        using (var reader = new PdfReader(original))
        using (var doc = new PdfDocument(reader))
        {
            var form = PdfAcroForm.GetAcroForm(doc, false);
            if (form is null || form.GetAllFormFields().Count == 0) return original;
            if (form.GetPdfObject().ContainsKey(PdfName.XFA)) throw new InvalidDataException("Los formularios XFA no son compatibles con esta versión.");
        }
        string copy = Path.Combine(directory, $"preview-{Guid.NewGuid():N}.pdf");
        try
        {
            using var reader = new PdfReader(original);
            using var writer = new PdfWriter(copy);
            using var doc = new PdfDocument(reader, writer);
            PdfAcroForm.GetAcroForm(doc, false)?.FlattenFields();
            return copy;
        }
        catch { if (File.Exists(copy)) File.Delete(copy); throw; }
    }
}
