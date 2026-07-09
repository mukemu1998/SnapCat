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
        return CreateTransformedBitmap(source, flipHorizontally, flipVertically, 0);
    }

    public static BitmapSource CreateTransformedBitmap(
        BitmapSource source,
        bool flipHorizontally,
        bool flipVertically,
        int rotationDegrees)
    {
        var normalizedRotation = NormalizeRotation(rotationDegrees);
        if (!flipHorizontally && !flipVertically && normalizedRotation == 0)
        {
            return source;
        }

        var rotatesSideways = normalizedRotation is 90 or 270;
        var pixelWidth = rotatesSideways ? source.PixelHeight : source.PixelWidth;
        var pixelHeight = rotatesSideways ? source.PixelWidth : source.PixelHeight;
        var renderBitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            var transform = new TransformGroup();
            transform.Children.Add(new TranslateTransform(-source.PixelWidth / 2d, -source.PixelHeight / 2d));
            transform.Children.Add(new ScaleTransform(flipHorizontally ? -1d : 1d, flipVertically ? -1d : 1d));
            transform.Children.Add(new RotateTransform(normalizedRotation));
            transform.Children.Add(new TranslateTransform(pixelWidth / 2d, pixelHeight / 2d));

            drawingContext.PushTransform(transform);
            drawingContext.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
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

    private static int NormalizeRotation(int rotationDegrees)
    {
        var normalized = rotationDegrees % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return normalized switch
        {
            < 45 => 0,
            < 135 => 90,
            < 225 => 180,
            < 315 => 270,
            _ => 0
        };
    }
}
