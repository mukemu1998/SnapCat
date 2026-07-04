using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapCat.Infrastructure.Services;

internal static class OcrImagePreprocessor
{
    public static IReadOnlyList<OcrImageVariant> CreateVariants(string imagePath, string workingDirectory)
    {
        var variants = new List<OcrImageVariant>
        {
            new("原图", imagePath, [6, 11])
        };

        using var original = new Bitmap(imagePath);
        using var scaled = ResizeBitmap(original, 2.0d);
        using var grayscale = ConvertToNormalizedGrayscale(scaled);

        var grayscalePath = Path.Combine(workingDirectory, "grayscale.png");
        grayscale.Save(grayscalePath, ImageFormat.Png);
        variants.Add(new OcrImageVariant("灰度增强", grayscalePath, [6, 11]));

        using var binary = ApplyOtsuThreshold(grayscale, invert: false);
        var binaryPath = Path.Combine(workingDirectory, "binary.png");
        binary.Save(binaryPath, ImageFormat.Png);
        variants.Add(new OcrImageVariant("黑字白底", binaryPath, [6, 11]));

        using var invertedBinary = ApplyOtsuThreshold(grayscale, invert: true);
        var invertedBinaryPath = Path.Combine(workingDirectory, "binary-inverted.png");
        invertedBinary.Save(invertedBinaryPath, ImageFormat.Png);
        variants.Add(new OcrImageVariant("白字黑底", invertedBinaryPath, [6, 11]));

        return variants;
    }

    private static Bitmap ResizeBitmap(Bitmap source, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, 0, 0, width, height);

        return resized;
    }

    private static Bitmap ConvertToNormalizedGrayscale(Bitmap source)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(source, 0, 0, source.Width, source.Height);

        var rectangle = new Rectangle(0, 0, result.Width, result.Height);
        var bitmapData = result.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = Math.Abs(bitmapData.Stride) * result.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(bitmapData.Scan0, buffer, 0, bytes);

            var min = byte.MaxValue;
            var max = byte.MinValue;

            for (var index = 0; index < buffer.Length; index += 4)
            {
                var blue = buffer[index];
                var green = buffer[index + 1];
                var red = buffer[index + 2];
                var gray = (byte)Math.Clamp((0.114d * blue) + (0.587d * green) + (0.299d * red), 0, 255);

                buffer[index] = gray;
                buffer[index + 1] = gray;
                buffer[index + 2] = gray;

                if (gray < min)
                {
                    min = gray;
                }

                if (gray > max)
                {
                    max = gray;
                }
            }

            if (max > min)
            {
                var range = max - min;
                for (var index = 0; index < buffer.Length; index += 4)
                {
                    var normalized = (byte)(((buffer[index] - min) * 255) / range);
                    buffer[index] = normalized;
                    buffer[index + 1] = normalized;
                    buffer[index + 2] = normalized;
                }
            }

            Marshal.Copy(buffer, 0, bitmapData.Scan0, bytes);
            return result;
        }
        finally
        {
            result.UnlockBits(bitmapData);
        }
    }

    private static Bitmap ApplyOtsuThreshold(Bitmap grayscale, bool invert)
    {
        var result = new Bitmap(grayscale.Width, grayscale.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(grayscale, 0, 0, grayscale.Width, grayscale.Height);

        var rectangle = new Rectangle(0, 0, result.Width, result.Height);
        var bitmapData = result.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = Math.Abs(bitmapData.Stride) * result.Height;
            var buffer = new byte[bytes];
            Marshal.Copy(bitmapData.Scan0, buffer, 0, bytes);

            var histogram = new int[256];
            for (var index = 0; index < buffer.Length; index += 4)
            {
                histogram[buffer[index]]++;
            }

            var threshold = CalculateOtsuThreshold(histogram, result.Width * result.Height);
            for (var index = 0; index < buffer.Length; index += 4)
            {
                var isForeground = buffer[index] <= threshold;
                var value = (byte)((isForeground ^ invert) ? 0 : 255);
                buffer[index] = value;
                buffer[index + 1] = value;
                buffer[index + 2] = value;
            }

            Marshal.Copy(buffer, 0, bitmapData.Scan0, bytes);
            return result;
        }
        finally
        {
            result.UnlockBits(bitmapData);
        }
    }

    private static int CalculateOtsuThreshold(int[] histogram, int totalPixels)
    {
        double sum = 0;
        for (var index = 0; index < histogram.Length; index++)
        {
            sum += index * histogram[index];
        }

        double sumBackground = 0;
        var backgroundWeight = 0;
        double maxVariance = 0;
        var threshold = 127;

        for (var index = 0; index < histogram.Length; index++)
        {
            backgroundWeight += histogram[index];
            if (backgroundWeight == 0)
            {
                continue;
            }

            var foregroundWeight = totalPixels - backgroundWeight;
            if (foregroundWeight == 0)
            {
                break;
            }

            sumBackground += index * histogram[index];
            var meanBackground = sumBackground / backgroundWeight;
            var meanForeground = (sum - sumBackground) / foregroundWeight;
            var variance = backgroundWeight * foregroundWeight * Math.Pow(meanBackground - meanForeground, 2);

            if (variance > maxVariance)
            {
                maxVariance = variance;
                threshold = index;
            }
        }

        return threshold;
    }
}

internal sealed record OcrImageVariant(
    string Label,
    string ImagePath,
    IReadOnlyList<int> PageSegmentationModes);
