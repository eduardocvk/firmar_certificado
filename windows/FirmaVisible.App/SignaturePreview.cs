using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FirmaVisible.Core;

namespace FirmaVisible.App;

public sealed class SignaturePreview : FrameworkElement
{
    private SignatureDesign design = SignatureDesign.Standard();
    private BitmapSource? image;
    private string name = "Tu nombre y apellidos", issuer = "Emisor del certificado";
    private double logicalWidth = 320, logicalHeight = 104;
    private static readonly Typeface Font = new(new FontFamily(new Uri("pack://application:,,,/"), "./Resources/#Noto Sans"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    public void Update(SignatureDesign value, string signerName, string issuerName, double width = 320, double height = 104)
    {
        if (!ReferenceEquals(design, value)) { image = value.ImagePng is { } png ? ImageNormalizer.Decode(png) : null; design = value; }
        name = signerName; issuer = issuerName; logicalWidth = width; logicalHeight = height; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        if (ActualWidth <= 0 || ActualHeight <= 0 || logicalWidth <= 0 || logicalHeight <= 0) return;
        context.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        context.PushTransform(new ScaleTransform(ActualWidth / logicalWidth, ActualHeight / logicalHeight));
        var rectangle = new Rect(0, 0, logicalWidth, logicalHeight);
        if (design.Decorated) context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(41, 89, 166)), .7), rectangle);
        try
        {
            var layout = SignatureArt.Layout((float)logicalWidth, (float)logicalHeight, design.Resolve(name, issuer, DateTimeOffset.Now), design.HasImage, design.TextSize);
            if (image is not null)
            {
                double scale = Math.Min(layout.ImageWidth / image.PixelWidth, layout.ImageHeight / image.PixelHeight);
                double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
                context.DrawImage(image, new Rect(6 + (layout.ImageWidth - w) / 2, 6 + (layout.ImageHeight - h) / 2, w, h));
            }
            double baseline = 6 + layout.FontSize;
            foreach (string line in layout.Lines)
            {
                var formatted = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, layout.FontSize,
                    new SolidColorBrush(Color.FromRgb(20, 41, 71)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(formatted, new Point(layout.TextX, baseline - formatted.Baseline)); baseline += layout.FontSize * 1.3;
            }
        }
        catch (ArgumentException ex)
        {
            var text = new FormattedText(ex.Message, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Font, 7, Brushes.Firebrick, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            text.MaxTextWidth = Math.Max(1, logicalWidth - 12); context.DrawText(text, new Point(6, 6));
        }
        context.Pop(); context.Pop();
    }
}
