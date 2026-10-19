using iText.Kernel.Geom;

namespace FirmaVisible.Core;

public sealed record Placement
{
    public int Page { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }

    public Placement(int page, double x, double y, double width, double height)
    {
        if (page < 0 || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width)
            || !double.IsFinite(height) || x < 0 || y < 0 || width <= 0 || height <= 0
            || x + width > 1.000001 || y + height > 1.000001)
            throw new ArgumentException("Selecciona un recuadro dentro de la página.");
        Page = page; X = x; Y = y; Width = width; Height = height;
    }

    public Rectangle ToPdf(Rectangle crop, int rotation)
    {
        var (left, bottom, w, h) = (crop.GetX(), crop.GetY(), crop.GetWidth(), crop.GetHeight());
        return (((rotation % 360) + 360) % 360) switch
        {
            0 => new((float)(left + X*w), (float)(bottom + (1-Y-Height)*h), (float)(Width*w), (float)(Height*h)),
            90 => new((float)(left + Y*w), (float)(bottom + X*h), (float)(Height*w), (float)(Width*h)),
            180 => new((float)(left + (1-X-Width)*w), (float)(bottom + Y*h), (float)(Width*w), (float)(Height*h)),
            270 => new((float)(left + (1-Y-Height)*w), (float)(bottom + (1-X-Width)*h), (float)(Height*w), (float)(Width*h)),
            _ => throw new ArgumentException("Rotación de página no admitida.")
        };
    }
}
