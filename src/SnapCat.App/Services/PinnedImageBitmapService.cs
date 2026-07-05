using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapCat.App.Services;

internal static class PinnedImageBitmapService
{
    public static BitmapSource LoadImage(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource CreateFlippedBitmap(BitmapSource source, bool flipHorizontally, bool flipVertically)
    {
        if (!flipHorizontally && !flipVertically)
        {
            return source;
        }

        var renderBitmap = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.PushTransform(new TranslateTransform(
                flipHorizontally ? source.PixelWidth : 0,
                flipVertically ? source.PixelHeight : 0));
            drawingContext.PushTransform(new ScaleTransform(
                flipHorizontally ? -1 : 1,
                flipVertically ? -1 : 1));
            drawingContext.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            drawingContext.Pop();
            drawingContext.Pop();
        }

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();
        return renderBitmap;
    }

    public static BitmapSource CreateTiledBitmap(BitmapSource source, PinnedArrayDirection direction, int tileCount)
    {
        var isHorizontal = direction is PinnedArrayDirection.Left or PinnedArrayDirection.Right;
        var pixelWidth = isHorizontal ? source.PixelWidth * tileCount : source.PixelWidth;
        var pixelHeight = isHorizontal ? source.PixelHeight : source.PixelHeight * tileCount;

        var renderBitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            for (var index = 0; index < tileCount; index++)
            {
                var x = isHorizontal ? source.PixelWidth * index : 0;
                var y = isHorizontal ? 0 : source.PixelHeight * index;
                drawingContext.DrawImage(source, new Rect(x, y, source.PixelWidth, source.PixelHeight));
            }
        }

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();
        return renderBitmap;
    }

    public static string WriteBitmapToTempFile(BitmapSource bitmapSource, string prefix)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "SnapCat");
        Directory.CreateDirectory(tempDirectory);

        var filePath = Path.Combine(tempDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
        return filePath;
    }
}
