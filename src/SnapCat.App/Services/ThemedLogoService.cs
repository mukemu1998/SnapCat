using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using DrawingSize = System.Drawing.Size;
using MediaColor = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App.Services;

public static class ThemedLogoService
{
    private static readonly Uri LogoUri = new("pack://application:,,,/Assets/SnapCat.png", UriKind.Absolute);
    private static readonly Dictionary<string, BitmapImage> ImageCache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapImage CreateLogoImage(MediaColor accent, MediaColor highlight)
    {
        var cacheKey = $"{accent}-{highlight}";
        if (ImageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        using var bitmap = CreateThemedBitmap(accent, highlight, 512);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        ImageCache[cacheKey] = image;
        return image;
    }

    public static Icon CreateTrayIcon(MediaColor accent, MediaColor highlight)
    {
        using var bitmap = CreateThemedBitmap(accent, highlight, 64);
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Bitmap CreateThemedBitmap(MediaColor accent, MediaColor highlight, int size)
    {
        using var source = LoadLogoBitmap();
        using var resized = new Bitmap(source, new DrawingSize(size, size));
        var output = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var accentColor = ToDrawingColor(accent);
        var highlightColor = ToDrawingColor(highlight);

        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var pixel = resized.GetPixel(x, y);
                if (pixel.A == 0)
                {
                    output.SetPixel(x, y, DrawingColor.FromArgb(0, 0, 0, 0));
                    continue;
                }

                var luminance = ((0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B)) / 255d;
                var themed = Mix(accentColor, highlightColor, Math.Clamp(luminance, 0.18d, 1d));
                output.SetPixel(x, y, DrawingColor.FromArgb(pixel.A, themed.R, themed.G, themed.B));
            }
        }

        return output;
    }

    private static Bitmap LoadLogoBitmap()
    {
        var resource = WpfApplication.GetResourceStream(LogoUri)
            ?? throw new FileNotFoundException("未找到 SnapCat logo 资源。", LogoUri.ToString());

        using var stream = resource.Stream;
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static DrawingColor ToDrawingColor(MediaColor color)
    {
        return DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static DrawingColor Mix(DrawingColor from, DrawingColor to, double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        return DrawingColor.FromArgb(
            255,
            (int)Math.Round(from.R + ((to.R - from.R) * t)),
            (int)Math.Round(from.G + ((to.G - from.G) * t)),
            (int)Math.Round(from.B + ((to.B - from.B) * t)));
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
