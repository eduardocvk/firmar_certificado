using System.Text;
using iText.IO.Font;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;

namespace FirmaVisible.Core;

public sealed record SignatureLayout(float FontSize, float TextX, float ImageWidth, float ImageHeight, IReadOnlyList<string> Lines, float Padding = 6);

public static class SignatureArt
{
    public const float MinimumDimension = 8;
    public const string SizeNotice = "El recuadro es demasiado pequeño. Amplíalo un poco para mostrar la firma.";
    private static readonly Lazy<byte[]> FontBytes = new(() =>
    {
        using var stream = typeof(SignatureArt).Assembly.GetManifestResourceStream("FirmaVisible.NotoSans.ttf")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    });
    private static PdfFont CreateFont() => PdfFontFactory.CreateFont(FontBytes.Value, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
    private static readonly Lazy<PdfFont> Metrics = new(CreateFont);
    private static readonly object FontLock = new();

    public static SignatureLayout Layout(float width, float height, string text, bool image, float textSize = 8)
    {
        if (!float.IsFinite(textSize) || textSize < 4 || textSize > 18) throw new ArgumentException("El tamaño del texto debe estar entre 4 y 18 pt.");
        if (!float.IsFinite(width) || !float.IsFinite(height) || width < MinimumDimension || height < MinimumDimension) throw new ArgumentException(SizeNotice);
        lock (FontLock)
        {
            var font = Metrics.Value;
            foreach (var rune in text.EnumerateRunes()) if (rune.Value != '\n' && !font.ContainsGlyph(rune.Value))
                throw new ArgumentException($"La fuente no admite el carácter U+{rune.Value:X4}. Cámbialo o utiliza una imagen.");
            float padding = Math.Min(6, Math.Min(width, height) * .08f);
            float iw = image ? string.IsNullOrWhiteSpace(text) ? width - 2 * padding : (width - 3 * padding) * .5f : 0;
            float x = image && !string.IsNullOrWhiteSpace(text) ? iw + 2 * padding : padding;
            if (string.IsNullOrWhiteSpace(text)) return new(10, x, iw, height - 2 * padding, [], padding);
            float available = width - x - padding;
            for (float size = textSize; ; size = Math.Max(.5f, size - .25f))
            {
                List<string> lines = []; bool fits = true;
                foreach (string paragraph in text.Split('\n'))
                {
                    string line = "";
                    foreach (var rune in paragraph.EnumerateRunes())
                    {
                        string next = line + rune;
                        if (font.GetWidth(next, size) > available)
                        {
                            if (line.Length == 0) { fits = false; break; }
                            int space = line.LastIndexOf(' ');
                            if (space > 0) { lines.Add(line[..space]); line = line[(space + 1)..] + rune; }
                            else { lines.Add(line); line = rune.ToString(); }
                        }
                        else line = next;
                    }
                    lines.Add(line);
                }
                if (fits && lines.Count * size * 1.3f <= height - 2 * padding) return new(size, x, iw, height - 2 * padding, lines, padding);
                if (size <= .5f) break;
            }
            throw new ArgumentException("El texto no cabe ni reduciendo la letra. Amplía el recuadro o acorta el texto.");
        }
    }

    public static PdfFormXObject Draw(PdfDocument doc, float width, float height, SignatureDesign design, string name, string issuer, DateTimeOffset time)
    {
        string text = design.Resolve(name, issuer, time);
        var layout = Layout(width, height, text, design.HasImage, design.TextSize);
        var form = new PdfFormXObject(new Rectangle(width, height));
        var canvas = new PdfCanvas(form, doc);
        if (design.Decorated)
        {
            canvas.SetStrokeColor(new DeviceRgb(41, 89, 166)).SetLineWidth(.7f).Rectangle(.5f, .5f, width - 1, height - 1).Stroke();
        }
        if (design.ImagePng is { } png)
        {
            var image = ImageDataFactory.Create(png);
            float scale = Math.Min(layout.ImageWidth / image.GetWidth(), layout.ImageHeight / image.GetHeight());
            float w = image.GetWidth() * scale, h = image.GetHeight() * scale;
            canvas.AddImageFittedIntoRectangle(image, new Rectangle(layout.Padding + (layout.ImageWidth - w) / 2, layout.Padding + (layout.ImageHeight - h) / 2, w, h), false);
        }
        if (layout.Lines.Count > 0)
        {
            var font = CreateFont(); font.SetSubset(false);
            canvas.SetFillColor(new DeviceRgb(20, 41, 71)); float y = height - layout.Padding - layout.FontSize;
            foreach (string line in layout.Lines)
            { canvas.BeginText().SetFontAndSize(font, layout.FontSize).MoveText(layout.TextX, y).ShowText(line).EndText(); y -= layout.FontSize * 1.3f; }
        }
        canvas.Release(); return form;
    }
}
