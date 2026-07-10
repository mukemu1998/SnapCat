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
        var sourceDpiX = source.DpiX > 0 ? source.DpiX : 96d;
        var sourceDpiY = source.DpiY > 0 ? source.DpiY : 96d;
        // DrawingVisual uses DIPs; pixel dimensions would magnify high-DPI images during rendering.
        var sourceWidth = source.PixelWidth * 96d / sourceDpiX;
        var sourceHeight = source.PixelHeight * 96d / sourceDpiY;
        var pixelWidth = rotatesSideways ? source.PixelHeight : source.PixelWidth;
        var pixelHeight = rotatesSideways ? source.PixelWidth : source.PixelHeight;
        var outputDpiX = rotatesSideways ? sourceDpiY : sourceDpiX;
        var outputDpiY = rotatesSideways ? sourceDpiX : sourceDpiY;
        var outputWidth = rotatesSideways ? sourceHeight : sourceWidth;
        var outputHeight = rotatesSideways ? sourceWidth : sourceHeight;
        var renderBitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            outputDpiX,
            outputDpiY,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            var transform = new TransformGroup();
            transform.Children.Add(new TranslateTransform(-sourceWidth / 2d, -sourceHeight / 2d));
            transform.Children.Add(new ScaleTransform(flipHorizontally ? -1d : 1d, flipVertically ? -1d : 1d));
            transform.Children.Add(new RotateTransform(normalizedRotation));
            transform.Children.Add(new TranslateTransform(outputWidth / 2d, outputHeight / 2d));

            drawingContext.PushTransform(transform);
            drawingContext.DrawImage(source, new Rect(0, 0, sourceWidth, sourceHeight));
            drawingContext.Pop();
        }

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();
        return renderBitmap;
    }

    public static BitmapSource CreateTiledBitmap(BitmapSource source, PinnedArrayDirection direction, int tileCount)
    {
        var isHorizontal = direction is PinnedArrayDirection.Left or PinnedArrayDirection.Right;
        var sourceDpiX = source.DpiX > 0 ? source.DpiX : 96d;
        var sourceDpiY = source.DpiY > 0 ? source.DpiY : 96d;
        var sourceWidth = source.PixelWidth * 96d / sourceDpiX;
        var sourceHeight = source.PixelHeight * 96d / sourceDpiY;
        var pixelWidth = isHorizontal ? source.PixelWidth * tileCount : source.PixelWidth;
        var pixelHeight = isHorizontal ? source.PixelHeight : source.PixelHeight * tileCount;

        var renderBitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            sourceDpiX,
            sourceDpiY,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            for (var index = 0; index < tileCount; index++)
            {
                var x = isHorizontal ? sourceWidth * index : 0;
                var y = isHorizontal ? 0 : sourceHeight * index;
                drawingContext.DrawImage(source, new Rect(x, y, sourceWidth, sourceHeight));
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
