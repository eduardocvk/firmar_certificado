using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FirmaVisible.App;

public static class ImageNormalizer
{
    public static byte[] Import(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Elige una imagen PNG o JPEG de menos de 16 MB.");
        byte[] source = File.ReadAllBytes(path); return Normalize(source);
    }
    public static byte[] Normalize(byte[] source)
    {
        if (source.Length > 16 * 1024 * 1024) throw new InvalidDataException("La imagen supera 16 MB.");
        using var input = new MemoryStream(source);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder) throw new InvalidDataException("Utiliza una imagen PNG o JPEG.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 100_000_000)
            throw new InvalidDataException("La imagen es demasiado grande.");
        int orientation = 1;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try { if (metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort value) orientation = value; } catch (NotSupportedException) { }
        }
        using var pixels = new MemoryStream(source);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = pixels;
        if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = Math.Min(1200, frame.PixelWidth);
        else image.DecodePixelHeight = Math.Min(1200, frame.PixelHeight);
        image.EndInit(); image.Freeze(); BitmapSource clean = image;
        var transforms = new TransformGroup();
        switch (orientation)
        {
            case 2: transforms.Children.Add(new ScaleTransform(-1, 1)); break;
            case 3: transforms.Children.Add(new RotateTransform(180)); break;
            case 4: transforms.Children.Add(new ScaleTransform(1, -1)); break;
            case 5: transforms.Children.Add(new RotateTransform(90)); transforms.Children.Add(new ScaleTransform(-1, 1)); break;
            case 6: transforms.Children.Add(new RotateTransform(90)); break;
            case 7: transforms.Children.Add(new RotateTransform(270)); transforms.Children.Add(new ScaleTransform(-1, 1)); break;
            case 8: transforms.Children.Add(new RotateTransform(270)); break;
        }
        if (transforms.Children.Count > 0) { clean = new TransformedBitmap(image, transforms); clean.Freeze(); }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(clean));
        using var output = new MemoryStream(); encoder.Save(output);
        if (output.Length > 4 * 1024 * 1024) throw new InvalidDataException("La imagen normalizada supera 4 MB. Utiliza una firma o logotipo más sencillo.");
        return output.ToArray();
    }
    public static BitmapSource Decode(byte[] png)
    {
        if (png.Length > 4 * 1024 * 1024) throw new InvalidDataException("Imagen de diseño excesiva.");
        using var stream = new MemoryStream(png);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames[0];
        if (image.PixelWidth > 1200 || image.PixelHeight > 1200) throw new InvalidDataException("Normaliza la imagen antes de guardar el diseño.");
        image.Freeze(); return image;
    }
}
