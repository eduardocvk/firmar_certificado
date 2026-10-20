using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FirmaVisible.App;

public sealed record RenderedPage(BitmapSource Image, int Count, double Width, double Height);

public static class PdfPreview
{
    public static async Task<RenderedPage> RenderAsync(string path, int pageIndex)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        var doc = await PdfDocument.LoadFromFileAsync(file);
        if (doc.PageCount == 0 || pageIndex < 0 || pageIndex >= doc.PageCount)
            throw new InvalidDataException("El PDF no contiene la página seleccionada.");
        using var page = doc.GetPage((uint)pageIndex);
        using var stream = new InMemoryRandomAccessStream();
        double scale = Math.Min(2.0, 1800 / Math.Max(page.Size.Width, page.Size.Height));
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width*scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height*scale))
        });
        stream.Seek(0);
        using var source = stream.AsStreamForRead();
        var image = new BitmapImage();
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = source; image.EndInit(); image.Freeze();
        // Windows renders in DIPs (96/in); layout and signing use PDF points (72/in).
        var points=FirmaVisible.Core.PdfEngine.PageSize(path,pageIndex);
        return new(image, (int)doc.PageCount, points.Width, points.Height);
    }
}
