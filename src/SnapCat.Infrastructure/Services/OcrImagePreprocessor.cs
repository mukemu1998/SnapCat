using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SnapCat.Infrastructure.Services;

internal static class OcrImagePreprocessor
{
    public static IReadOnlyList<OcrImageVariant> CreateVariants(string imagePath, string workingDirectory)
    {
        using var original = new Bitmap(imagePath);
        var sourceWidth = original.Width;
        var sourceHeight = original.Height;
        var padding = CalculatePadding(original);
        using var padded = AddPadding(original, padding, Color.White);
        using var scaled2x = ResizeBitmap(padded, 2.0d);
        using var scaled3x = ResizeBitmap(padded, 3.0d);
        var adaptiveScale = CalculateAdaptiveScale(padded);
        using var grayscale2x = ConvertToNormalizedGrayscale(scaled2x);
        using var grayscale3x = ConvertToNormalizedGrayscale(scaled3x);
        using var contrast2x = ApplyAutoContrast(grayscale2x, clipPercentage: 1.8d);
        using var sharpened2x = ApplySharpen(contrast2x);
        using var contrast3x = ApplyAutoContrast(grayscale3x, clipPercentage: 1.4d);
        using var sharpened3x = ApplySharpen(contrast3x);
        using var invertedContrast2x = InvertGrayscale(contrast2x);
        using var invertedContrast3x = InvertGrayscale(contrast3x);

        var variants = new List<OcrImageVariant>();
        var paddedOriginalPath = Path.Combine(workingDirectory, "padded-original.png");
        padded.Save(paddedOriginalPath, ImageFormat.Png);
        variants.Add(CreateVariant("留白原图", paddedOriginalPath, [6, 11], 1.0d, padding, sourceWidth, sourceHeight));

        var grayscalePath = Path.Combine(workingDirectory, "grayscale-2x.png");
        grayscale2x.Save(grayscalePath, ImageFormat.Png);
        variants.Add(CreateVariant("灰度增强 2x", grayscalePath, [6, 11, 13], 2.0d, padding, sourceWidth, sourceHeight));

        var contrastPath = Path.Combine(workingDirectory, "contrast-2x.png");
        contrast2x.Save(contrastPath, ImageFormat.Png);
        variants.Add(CreateVariant("强对比灰度 2x", contrastPath, [6, 11, 13], 2.0d, padding, sourceWidth, sourceHeight));

        var sharpenedPath = Path.Combine(workingDirectory, "sharpened-2x.png");
        sharpened2x.Save(sharpenedPath, ImageFormat.Png);
        variants.Add(CreateVariant("锐化增强 2x", sharpenedPath, [6, 11, 13], 2.0d, padding, sourceWidth, sourceHeight));

        var contrast3xPath = Path.Combine(workingDirectory, "contrast-3x.png");
        contrast3x.Save(contrast3xPath, ImageFormat.Png);
        variants.Add(CreateVariant("强对比灰度 3x", contrast3xPath, [6, 11, 13], 3.0d, padding, sourceWidth, sourceHeight));

        var sharpened3xPath = Path.Combine(workingDirectory, "sharpened-3x.png");
        sharpened3x.Save(sharpened3xPath, ImageFormat.Png);
        variants.Add(CreateVariant("锐化增强 3x", sharpened3xPath, [6, 11, 13], 3.0d, padding, sourceWidth, sourceHeight));

        if (adaptiveScale > 3.0d)
        {
            using var adaptiveScaled = ResizeBitmap(padded, adaptiveScale);
            using var adaptiveGrayscale = ConvertToNormalizedGrayscale(adaptiveScaled);
            using var adaptiveContrast = ApplyAutoContrast(adaptiveGrayscale, clipPercentage: 1.2d);
            using var adaptiveSharpened = ApplySharpen(adaptiveContrast);
            var adaptivePath = Path.Combine(workingDirectory, "adaptive-small-text.png");
            adaptiveSharpened.Save(adaptivePath, ImageFormat.Png);
            variants.Add(CreateVariant($"小字增强 {adaptiveScale:0.#}x", adaptivePath, [6, 11, 13], adaptiveScale, padding, sourceWidth, sourceHeight));
        }

        var invertedContrast2xPath = Path.Combine(workingDirectory, "inverted-contrast-2x.png");
        invertedContrast2x.Save(invertedContrast2xPath, ImageFormat.Png);
        variants.Add(CreateVariant("反色增强 2x", invertedContrast2xPath, [6, 11, 13], 2.0d, padding, sourceWidth, sourceHeight));

        var invertedContrast3xPath = Path.Combine(workingDirectory, "inverted-contrast-3x.png");
        invertedContrast3x.Save(invertedContrast3xPath, ImageFormat.Png);
        variants.Add(CreateVariant("反色增强 3x", invertedContrast3xPath, [6, 11, 13], 3.0d, padding, sourceWidth, sourceHeight));

        using var binary = ApplyOtsuThreshold(sharpened2x, invert: false);
        var binaryPath = Path.Combine(workingDirectory, "binary.png");
        binary.Save(binaryPath, ImageFormat.Png);
        variants.Add(CreateVariant("黑字白底", binaryPath, [6, 11], 2.0d, padding, sourceWidth, sourceHeight));

        using var thickenedBinary = DilateBinary(binary, radius: 1);
        var thickenedBinaryPath = Path.Combine(workingDirectory, "binary-thickened.png");
        thickenedBinary.Save(thickenedBinaryPath, ImageFormat.Png);
        variants.Add(CreateVariant("细字加粗", thickenedBinaryPath, [6, 11], 2.0d, padding, sourceWidth, sourceHeight));

        using var invertedBinary = ApplyOtsuThreshold(sharpened2x, invert: true);
        var invertedBinaryPath = Path.Combine(workingDirectory, "binary-inverted.png");
        invertedBinary.Save(invertedBinaryPath, ImageFormat.Png);
        variants.Add(CreateVariant("白字黑底", invertedBinaryPath, [6, 11], 2.0d, padding, sourceWidth, sourceHeight));

        return variants;
    }

    private static OcrImageVariant CreateVariant(
        string label,
        string imagePath,
        IReadOnlyList<int> pageSegmentationModes,
        double scale,
        int padding,
        int sourceWidth,
        int sourceHeight)
    {
        return new OcrImageVariant(
            label,
            imagePath,
            pageSegmentationModes,
            scale,
            padding,
            sourceWidth,
            sourceHeight);
    }

    private static int CalculatePadding(Bitmap source)
    {
        var baseline = Math.Min(source.Width, source.Height);
        return Math.Clamp(baseline / 18, 12, 48);
    }

    private static double CalculateAdaptiveScale(Bitmap source)
    {
        var baseline = Math.Min(source.Width, source.Height);
        return baseline switch
        {
            <= 180 => 4.5d,
            <= 320 => 4.0d,
            <= 520 => 3.5d,
            _ => 3.0d
        };
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

    private static Bitmap AddPadding(Bitmap source, int padding, Color backgroundColor)
    {
        var padded = new Bitmap(
            source.Width + (padding * 2),
            source.Height + (padding * 2),
            PixelFormat.Format32bppArgb);

        using var graphics = Graphics.FromImage(padded);
        graphics.Clear(backgroundColor);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(source, padding, padding, source.Width, source.Height);

        return padded;
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

    private static Bitmap ApplyAutoContrast(Bitmap grayscale, double clipPercentage)
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

            var totalPixels = result.Width * result.Height;
            var clippedPixels = Math.Max(1, (int)Math.Round(totalPixels * (clipPercentage / 100d)));
            var lowerBound = FindHistogramBound(histogram, clippedPixels, fromStart: true);
            var upperBound = FindHistogramBound(histogram, clippedPixels, fromStart: false);

            if (upperBound <= lowerBound)
            {
                return result;
            }

            var range = upperBound - lowerBound;
            for (var index = 0; index < buffer.Length; index += 4)
            {
                var value = buffer[index];
                var normalized = (byte)Math.Clamp(((value - lowerBound) * 255) / range, 0, 255);
                buffer[index] = normalized;
                buffer[index + 1] = normalized;
                buffer[index + 2] = normalized;
            }

            Marshal.Copy(buffer, 0, bitmapData.Scan0, bytes);
            return result;
        }
        finally
        {
            result.UnlockBits(bitmapData);
        }
    }

    private static Bitmap ApplySharpen(Bitmap grayscale)
    {
        var result = new Bitmap(grayscale.Width, grayscale.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(grayscale, 0, 0, grayscale.Width, grayscale.Height);

        var sourceRectangle = new Rectangle(0, 0, grayscale.Width, grayscale.Height);
        var sourceData = grayscale.LockBits(sourceRectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var resultData = result.LockBits(sourceRectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = Math.Abs(sourceData.Stride) * grayscale.Height;
            var sourceBuffer = new byte[bytes];
            var resultBuffer = new byte[bytes];
            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            Array.Copy(sourceBuffer, resultBuffer, bytes);
            var kernel = new[,]
            {
                { 0, -1, 0 },
                { -1, 5, -1 },
                { 0, -1, 0 }
            };

            for (var y = 1; y < grayscale.Height - 1; y++)
            {
                for (var x = 1; x < grayscale.Width - 1; x++)
                {
                    var sum = 0;
                    for (var ky = -1; ky <= 1; ky++)
                    {
                        for (var kx = -1; kx <= 1; kx++)
                        {
                            var sampleIndex = ((y + ky) * sourceData.Stride) + ((x + kx) * 4);
                            sum += sourceBuffer[sampleIndex] * kernel[ky + 1, kx + 1];
                        }
                    }

                    var targetIndex = (y * resultData.Stride) + (x * 4);
                    var value = (byte)Math.Clamp(sum, 0, 255);
                    resultBuffer[targetIndex] = value;
                    resultBuffer[targetIndex + 1] = value;
                    resultBuffer[targetIndex + 2] = value;
                    resultBuffer[targetIndex + 3] = sourceBuffer[targetIndex + 3];
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);
            return result;
        }
        finally
        {
            grayscale.UnlockBits(sourceData);
            result.UnlockBits(resultData);
        }
    }

    private static Bitmap InvertGrayscale(Bitmap grayscale)
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

            for (var index = 0; index < buffer.Length; index += 4)
            {
                var value = (byte)(255 - buffer[index]);
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

    private static Bitmap DilateBinary(Bitmap binary, int radius)
    {
        var result = new Bitmap(binary.Width, binary.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.DrawImage(binary, 0, 0, binary.Width, binary.Height);

        var rectangle = new Rectangle(0, 0, binary.Width, binary.Height);
        var sourceData = binary.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var resultData = result.LockBits(rectangle, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            var bytes = Math.Abs(sourceData.Stride) * binary.Height;
            var sourceBuffer = new byte[bytes];
            var resultBuffer = new byte[bytes];
            Marshal.Copy(sourceData.Scan0, sourceBuffer, 0, bytes);

            for (var y = 0; y < binary.Height; y++)
            {
                for (var x = 0; x < binary.Width; x++)
                {
                    var hasForeground = false;

                    for (var offsetY = -radius; offsetY <= radius && !hasForeground; offsetY++)
                    {
                        var sampleY = y + offsetY;
                        if (sampleY < 0 || sampleY >= binary.Height)
                        {
                            continue;
                        }

                        for (var offsetX = -radius; offsetX <= radius; offsetX++)
                        {
                            var sampleX = x + offsetX;
                            if (sampleX < 0 || sampleX >= binary.Width)
                            {
                                continue;
                            }

                            var sampleIndex = (sampleY * sourceData.Stride) + (sampleX * 4);
                            if (sourceBuffer[sampleIndex] == 0)
                            {
                                hasForeground = true;
                                break;
                            }
                        }
                    }

                    var targetIndex = (y * resultData.Stride) + (x * 4);
                    var value = (byte)(hasForeground ? 0 : 255);
                    resultBuffer[targetIndex] = value;
                    resultBuffer[targetIndex + 1] = value;
                    resultBuffer[targetIndex + 2] = value;
                    resultBuffer[targetIndex + 3] = 255;
                }
            }

            Marshal.Copy(resultBuffer, 0, resultData.Scan0, bytes);
            return result;
        }
        finally
        {
            binary.UnlockBits(sourceData);
            result.UnlockBits(resultData);
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

    private static int FindHistogramBound(int[] histogram, int clippedPixels, bool fromStart)
    {
        var accumulated = 0;
        if (fromStart)
        {
            for (var index = 0; index < histogram.Length; index++)
            {
                accumulated += histogram[index];
                if (accumulated >= clippedPixels)
                {
                    return index;
                }
            }

            return 0;
        }

        for (var index = histogram.Length - 1; index >= 0; index--)
        {
            accumulated += histogram[index];
            if (accumulated >= clippedPixels)
            {
                return index;
            }
        }

        return histogram.Length - 1;
    }
}

internal sealed record OcrImageVariant(
    string Label,
    string ImagePath,
    IReadOnlyList<int> PageSegmentationModes,
    double Scale,
    int Padding,
    int SourceWidth,
    int SourceHeight);
