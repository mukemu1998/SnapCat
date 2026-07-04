using System.Text;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class EnhancedTesseractOcrService : IOcrService
{
    private readonly TesseractCliOcrService _tesseractCliOcrService;

    public EnhancedTesseractOcrService(TesseractCliOcrService tesseractCliOcrService)
    {
        _tesseractCliOcrService = tesseractCliOcrService;
    }

    public async Task<OcrResult> RecognizeAsync(
        string imagePath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(settings.OcrEngine, "tesseract-cli", StringComparison.Ordinal))
        {
            return await _tesseractCliOcrService.RecognizeAsync(imagePath, settings, cancellationToken);
        }

        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "SnapCat",
            "ocr",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workingDirectory);

        try
        {
            var candidates = OcrImagePreprocessor.CreateVariants(imagePath, workingDirectory);
            var attempts = new List<OcrAttempt>();

            foreach (var candidate in candidates)
            {
                foreach (var pageSegmentationMode in candidate.PageSegmentationModes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await _tesseractCliOcrService.RecognizeAsync(
                        candidate.ImagePath,
                        settings,
                        pageSegmentationMode,
                        cancellationToken);

                    attempts.Add(new OcrAttempt(
                        candidate.Label,
                        candidate.ImagePath,
                        pageSegmentationMode,
                        result,
                        ScoreText(result.Text, settings)));
                }
            }

            var best = attempts
                .Where(static attempt => attempt.Result.Success && !string.IsNullOrWhiteSpace(attempt.Result.Text))
                .OrderByDescending(static attempt => attempt.Score)
                .ThenByDescending(static attempt => attempt.Result.Text.Length)
                .FirstOrDefault();

            var debugSummary = BuildDebugSummary(imagePath, settings, attempts, best);

            if (best is not null)
            {
                return OcrResult.FromText(
                    NormalizeText(best.Result.Text),
                    "enhanced-tesseract",
                    debugSummary);
            }

            var firstError = attempts
                .Select(static attempt => attempt.Result.ErrorMessage)
                .FirstOrDefault(static message => !string.IsNullOrWhiteSpace(message));

            return OcrResult.FromError(
                firstError ?? "本地 OCR 未能识别出可用文本。",
                "enhanced-tesseract",
                debugSummary);
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private static string BuildDebugSummary(
        string imagePath,
        AppSettings settings,
        IReadOnlyList<OcrAttempt> attempts,
        OcrAttempt? best)
    {
        var builder = new StringBuilder();
        builder.AppendLine("引擎：增强本地 OCR（Tesseract 多轮识别）");
        builder.AppendLine($"图片：{imagePath}");
        builder.AppendLine($"语言：{settings.TesseractLanguage}");
        builder.AppendLine($"尝试次数：{attempts.Count}");

        if (best is not null)
        {
            builder.AppendLine($"最终选择：{best.VariantLabel} / PSM {best.PageSegmentationMode} / 分数 {best.Score:F1}");
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
                ? CreatePreview(attempt.Result.Text)
                : attempt.Result.ErrorMessage;

            builder.AppendLine(
                $"- {attempt.VariantLabel} / PSM {attempt.PageSegmentationMode} / {status} / 分数 {attempt.Score:F1}");
            builder.AppendLine($"  预览：{preview}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string CreatePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        var singleLine = text.ReplaceLineEndings(" ").Trim();
        const int maxLength = 80;
        return singleLine.Length <= maxLength
            ? singleLine
            : $"{singleLine[..maxLength]}...";
    }

    private static double ScoreText(string text, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return double.MinValue;
        }

        var normalized = NormalizeText(text);
        if (normalized.Length < 2)
        {
            return -1000;
        }

        var totalLength = normalized.Length;
        var lettersOrDigits = 0;
        var spaces = 0;
        var cjk = 0;
        var punctuation = 0;
        var suspicious = 0;
        var repeatPenalty = 0;
        var previous = '\0';
        var repeatedCount = 0;

        foreach (var character in normalized)
        {
            if (character == previous)
            {
                repeatedCount++;
                if (repeatedCount >= 3)
                {
                    repeatPenalty++;
                }
            }
            else
            {
                repeatedCount = 0;
                previous = character;
            }

            if (char.IsLetterOrDigit(character))
            {
                lettersOrDigits++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                spaces++;
                continue;
            }

            if (IsCjk(character))
            {
                cjk++;
                continue;
            }

            if (IsCommonPunctuation(character))
            {
                punctuation++;
                continue;
            }

            suspicious++;
        }

        var expectsCjk = settings.TesseractLanguage.Contains("chi", StringComparison.OrdinalIgnoreCase)
            || settings.TesseractLanguage.Contains("jpn", StringComparison.OrdinalIgnoreCase)
            || settings.TesseractLanguage.Contains("kor", StringComparison.OrdinalIgnoreCase);

        return (totalLength * 1.2d)
            + (lettersOrDigits * 1.6d)
            + (spaces * 0.2d)
            + (punctuation * 0.8d)
            + (expectsCjk ? cjk * 2.2d : cjk * 0.6d)
            - (suspicious * 4.5d)
            - (repeatPenalty * 3.0d);
    }

    private static string NormalizeText(string text)
    {
        var lines = text
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.Trim());

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsCjk(char character)
    {
        return character is >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff';
    }

    private static bool IsCommonPunctuation(char character)
    {
        return character is '.' or ',' or '!' or '?' or ':' or ';' or '\'' or '"' or '-'
            or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '#'
            or '&' or '%' or '+' or '=' or '_' or '*'
            or '，' or '。' or '！' or '？' or '：' or '；' or '（' or '）'
            or '【' or '】' or '「' or '」' or '、' or '《' or '》' or '“' or '”';
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
        string VariantPath,
        int PageSegmentationMode,
        OcrResult Result,
        double Score);
}
