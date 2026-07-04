using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
                    "windows-media-ocr",
                    debugSummary);
            }

            var firstError = attempts
                .Select(static attempt => attempt.Result.ErrorMessage)
                .FirstOrDefault(static message => !string.IsNullOrWhiteSpace(message));

            return SnapCatOcrResult.FromError(
                firstError ?? "系统内置 OCR 未能识别出可用文本。",
                "windows-media-ocr",
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
                    SnapCatOcrResult.FromError("当前系统不支持该 OCR 语言。", "windows-media-ocr"),
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

            var debugSummary =
                $"引擎：系统内置 OCR{Environment.NewLine}" +
                $"候选图：{variant.Label}{Environment.NewLine}" +
                $"语言：{language.LanguageTag}{Environment.NewLine}" +
                $"尺寸：{prepared.Width}x{prepared.Height}";

            return string.IsNullOrWhiteSpace(text)
                ? new OcrAttempt(
                    variant.Label,
                    language.LanguageTag,
                    SnapCatOcrResult.FromError("OCR 识别结果为空。", "windows-media-ocr", debugSummary),
                    double.MinValue)
                : new OcrAttempt(
                    variant.Label,
                    language.LanguageTag,
                    SnapCatOcrResult.FromText(text, "windows-media-ocr", debugSummary),
                    OcrRecognitionHeuristics.ScoreText(text, settings));
        }
        catch (Exception ex)
        {
            return new OcrAttempt(
                variant.Label,
                language.LanguageTag,
                SnapCatOcrResult.FromError($"系统内置 OCR 失败：{ex.Message}", "windows-media-ocr"),
                double.MinValue);
        }
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
        builder.AppendLine("引擎：系统内置 OCR");
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
