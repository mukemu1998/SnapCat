using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using SnapCat.Core.Services;
using SnapCat.Core.Models;
using SnapCatOcrResult = SnapCat.Core.Models.OcrResult;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapCat.Infrastructure.Services;

public sealed class WindowsMediaOcrService : IOcrService
{
    public async Task<SnapCatOcrResult> RecognizeAsync(
        string imagePath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "SnapCat",
            "ocr",
            "windows",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workingDirectory);

        try
        {
            var variants = OcrImagePreprocessor.CreateVariants(imagePath, workingDirectory);
            var languageCandidates = ResolveLanguages(settings).ToList();
            var attempts = new List<OcrAttempt>();

            foreach (var variant in variants)
            {
                foreach (var language in languageCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var attempt = await RecognizeVariantAsync(variant, language, settings, cancellationToken);
                    attempts.Add(attempt);
                }
            }

            var best = attempts
                .Where(static attempt => attempt.Result.Success && !string.IsNullOrWhiteSpace(attempt.Result.Text))
                .OrderByDescending(static attempt => attempt.Score)
                .ThenByDescending(static attempt => attempt.Result.Text.Length)
                .FirstOrDefault();

            var debugSummary = BuildDebugSummary(imagePath, attempts, best);
            if (best is not null)
            {
                return SnapCatOcrResult.FromText(
                    OcrRecognitionHeuristics.NormalizeText(best.Result.Text),
                    "enhanced-windows-ocr",
                    debugSummary,
                    best.Result.Regions);
            }

            var firstError = attempts
                .Select(static attempt => attempt.Result.ErrorMessage)
                .FirstOrDefault(static message => !string.IsNullOrWhiteSpace(message));

            return SnapCatOcrResult.FromError(
                firstError ?? "本地轻量增强版未能识别出可用文本。",
                "enhanced-windows-ocr",
                debugSummary);
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private static IEnumerable<Language> ResolveLanguages(AppSettings settings)
    {
        var candidates = new List<string>();
        var tesseractLanguages = (settings.TesseractLanguage ?? string.Empty)
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var item in tesseractLanguages)
        {
            switch (item)
            {
                case "chi_sim":
                    candidates.Add("zh-Hans");
                    break;
                case "chi_tra":
                    candidates.Add("zh-Hant");
                    break;
                case "eng":
                    candidates.Add("en-US");
                    break;
                case "jpn":
                    candidates.Add("ja-JP");
                    break;
                case "kor":
                    candidates.Add("ko-KR");
                    break;
                case "vie":
                    candidates.Add("vi-VN");
                    break;
                case "fra":
                case "fre":
                    candidates.Add("fr-FR");
                    break;
                case "deu":
                case "ger":
                    candidates.Add("de-DE");
                    break;
                case "rus":
                    candidates.Add("ru-RU");
                    break;
            }
        }

        if (candidates.Count == 0)
        {
            candidates.Add("zh-Hans");
            candidates.Add("en-US");
        }

        foreach (var tag in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var language = new Language(tag);
            if (OcrEngine.IsLanguageSupported(language))
            {
                yield return language;
            }
        }

        var userProfileEngine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (userProfileEngine is not null)
        {
            yield return userProfileEngine.RecognizerLanguage;
        }
        else
        {
            var fallback = new Language("en-US");
            if (OcrEngine.IsLanguageSupported(fallback))
            {
                yield return fallback;
            }
        }
    }

    private static async Task<OcrAttempt> RecognizeVariantAsync(
        OcrImageVariant variant,
        Language language,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is null)
            {
                return new OcrAttempt(
                    variant.Label,
                    language.LanguageTag,
                    SnapCatOcrResult.FromError("当前系统不支持该 OCR 语言。", "enhanced-windows-ocr"),
                    double.MinValue);
            }

            using var bitmap = new Bitmap(variant.ImagePath);
            using var prepared = ResizeForOcr(bitmap, (int)OcrEngine.MaxImageDimension);
            using var memoryStream = new MemoryStream();
            prepared.Save(memoryStream, ImageFormat.Png);
            memoryStream.Position = 0;

            using var randomAccessStream = memoryStream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var result = await engine.RecognizeAsync(softwareBitmap).AsTask(cancellationToken);
            var text = string.Join(
                Environment.NewLine,
                result.Lines
                    .Select(static line => line.Text?.Trim())
                    .Where(static line => !string.IsNullOrWhiteSpace(line)));
            var regions = CreateTextRegions(result, variant, bitmap.Width, bitmap.Height, prepared.Width, prepared.Height);

            var debugSummary =
                $"引擎：本地轻量增强版{Environment.NewLine}" +
                $"候选图：{variant.Label}{Environment.NewLine}" +
                $"语言：{language.LanguageTag}{Environment.NewLine}" +
                $"尺寸：{prepared.Width}x{prepared.Height}";

            return string.IsNullOrWhiteSpace(text)
                ? new OcrAttempt(
                    variant.Label,
                    language.LanguageTag,
                    SnapCatOcrResult.FromError("OCR 识别结果为空。", "enhanced-windows-ocr", debugSummary),
                    double.MinValue)
                : new OcrAttempt(
                    variant.Label,
                    language.LanguageTag,
                    SnapCatOcrResult.FromText(text, "enhanced-windows-ocr", debugSummary, regions),
                    OcrRecognitionHeuristics.ScoreText(text, settings));
        }
        catch (Exception ex)
        {
            return new OcrAttempt(
                variant.Label,
                language.LanguageTag,
                SnapCatOcrResult.FromError($"本地轻量增强版失败：{ex.Message}", "enhanced-windows-ocr"),
                double.MinValue);
        }
    }

    private static IReadOnlyList<OcrTextRegion> CreateTextRegions(
        Windows.Media.Ocr.OcrResult result,
        OcrImageVariant variant,
        int variantBitmapWidth,
        int variantBitmapHeight,
        int preparedWidth,
        int preparedHeight)
    {
        var resizeScaleX = preparedWidth / (double)Math.Max(1, variantBitmapWidth);
        var resizeScaleY = preparedHeight / (double)Math.Max(1, variantBitmapHeight);
        var regions = new List<OcrTextRegion>();

        foreach (var line in result.Lines)
        {
            var words = line.Words
                .Where(static word => !string.IsNullOrWhiteSpace(word.Text))
                .ToList();

            if (words.Count == 0)
            {
                continue;
            }

            foreach (var word in words)
            {
                var mapped = MapPreparedRectToSource(word.BoundingRect, variant, resizeScaleX, resizeScaleY);
                if (mapped.Width <= 1 || mapped.Height <= 1)
                {
                    continue;
                }

                regions.AddRange(SplitWordRegionToTextElements(word.Text.Trim(), mapped));
            }
        }

        return regions;
    }

    private static OcrTextRegion MapPreparedRectToSource(
        Windows.Foundation.Rect rect,
        OcrImageVariant variant,
        double resizeScaleX,
        double resizeScaleY)
    {
        var sourceX = ((rect.X / resizeScaleX) / variant.Scale) - variant.Padding;
        var sourceY = ((rect.Y / resizeScaleY) / variant.Scale) - variant.Padding;
        var sourceWidth = (rect.Width / resizeScaleX) / variant.Scale;
        var sourceHeight = (rect.Height / resizeScaleY) / variant.Scale;

        const double pad = 3.0d;
        sourceX -= pad;
        sourceY -= pad;
        sourceWidth += pad * 2;
        sourceHeight += pad * 2;

        var left = Math.Clamp(sourceX, 0, variant.SourceWidth);
        var top = Math.Clamp(sourceY, 0, variant.SourceHeight);
        var right = Math.Clamp(sourceX + sourceWidth, 0, variant.SourceWidth);
        var bottom = Math.Clamp(sourceY + sourceHeight, 0, variant.SourceHeight);

        return new OcrTextRegion(
            string.Empty,
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }

    private static IReadOnlyList<OcrTextRegion> SplitWordRegionToTextElements(string text, OcrTextRegion bounds)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<OcrTextRegion>();
        }

        var elements = StringInfo.GetTextElementEnumerator(text);
        var textElements = new List<string>();
        while (elements.MoveNext())
        {
            if (elements.GetTextElement() is string element && !string.IsNullOrWhiteSpace(element))
            {
                textElements.Add(element);
            }
        }

        if (textElements.Count <= 1)
        {
            return [new OcrTextRegion(text.Trim(), bounds.X, bounds.Y, bounds.Width, bounds.Height)];
        }

        var regions = new List<OcrTextRegion>(textElements.Count);
        var elementWidth = bounds.Width / textElements.Count;
        for (var index = 0; index < textElements.Count; index++)
        {
            var left = bounds.X + (elementWidth * index);
            var right = index == textElements.Count - 1
                ? bounds.X + bounds.Width
                : bounds.X + (elementWidth * (index + 1));
            regions.Add(new OcrTextRegion(
                textElements[index],
                left,
                bounds.Y,
                Math.Max(1, right - left),
                bounds.Height));
        }

        return regions;
    }

    private static Bitmap ResizeForOcr(Bitmap source, int maxDimension)
    {
        if (source.Width <= maxDimension && source.Height <= maxDimension)
        {
            return new Bitmap(source);
        }

        var scale = Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height);
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

    private static string BuildDebugSummary(
        string imagePath,
        IReadOnlyList<OcrAttempt> attempts,
        OcrAttempt? best)
    {
        var builder = new StringBuilder();
        builder.AppendLine("引擎：本地轻量增强版");
        builder.AppendLine($"图片：{imagePath}");
        builder.AppendLine($"尝试次数：{attempts.Count}");

        if (best is not null)
        {
            builder.AppendLine($"最终选择：{best.VariantLabel} / {best.LanguageTag} / 分数 {best.Score:F1}");
        }
        else
        {
            builder.AppendLine("最终选择：无可用结果");
        }

        builder.AppendLine();
        builder.AppendLine("候选结果：");

        foreach (var attempt in attempts.OrderByDescending(static item => item.Score))
        {
            var status = attempt.Result.Success && !string.IsNullOrWhiteSpace(attempt.Result.Text)
                ? "成功"
                : "失败";

            var preview = attempt.Result.Success
                ? OcrRecognitionHeuristics.CreatePreview(attempt.Result.Text)
                : attempt.Result.ErrorMessage;

            builder.AppendLine($"- {attempt.VariantLabel} / {attempt.LanguageTag} / {status} / 分数 {attempt.Score:F1}");
            builder.AppendLine($"  预览：{preview}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Ignore temporary cleanup failures.
        }
    }

    private sealed record OcrAttempt(
        string VariantLabel,
        string LanguageTag,
        SnapCatOcrResult Result,
        double Score);
}
