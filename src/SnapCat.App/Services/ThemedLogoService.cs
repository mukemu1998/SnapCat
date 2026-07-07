using System.Drawing;
using System.Drawing.Drawing2D;
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
        try
        {
            var resource = WpfApplication.GetResourceStream(LogoUri);
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var source = new Bitmap(stream);
                return new Bitmap(source);
            }
        }
        catch
        {
            // 启动早期 pack 资源可能还没就绪，继续走发布包旁路资源。
        }

        foreach (var path in EnumerateLogoFileCandidates())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                using var source = new Bitmap(path);
                return new Bitmap(source);
            }
            catch
            {
                // 单个候选损坏不影响启动，继续尝试下一个候选。
            }
        }

        return CreateFallbackLogoBitmap();
    }

    private static IEnumerable<string> EnumerateLogoFileCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "SnapCat.png");
        yield return Path.Combine(AppContext.BaseDirectory, "SnapCat.png");
    }

    private static Bitmap CreateFallbackLogoBitmap()
    {
        var bitmap = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(DrawingColor.Transparent);

        using var pen = new Pen(DrawingColor.White, 36)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        using var fill = new SolidBrush(DrawingColor.FromArgb(255, 255, 255, 255));
        using var facePath = CreateRoundedRectanglePath(new Rectangle(86, 116, 340, 280), 64);
        graphics.DrawPath(pen, facePath);
        graphics.DrawLine(pen, 158, 128, 206, 72);
        graphics.DrawLine(pen, 354, 128, 306, 72);
        graphics.FillEllipse(fill, 178, 238, 38, 38);
        graphics.FillEllipse(fill, 296, 238, 38, 38);
        graphics.FillEllipse(fill, 242, 288, 28, 22);
        graphics.DrawArc(pen, 210, 292, 48, 42, 20, 120);
        graphics.DrawArc(pen, 254, 292, 48, 42, 40, 120);
        return bitmap;
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
