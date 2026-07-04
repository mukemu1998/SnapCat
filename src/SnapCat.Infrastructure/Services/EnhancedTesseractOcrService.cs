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
                    OcrRecognitionHeuristics.ScoreText(result.Text, settings)));
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
                    OcrRecognitionHeuristics.NormalizeText(best.Result.Text),
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
                ? OcrRecognitionHeuristics.CreatePreview(attempt.Result.Text)
                : attempt.Result.ErrorMessage;

            builder.AppendLine(
                $"- {attempt.VariantLabel} / PSM {attempt.PageSegmentationMode} / {status} / 分数 {attempt.Score:F1}");
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
        string VariantPath,
        int PageSegmentationMode,
        OcrResult Result,
        double Score);
}
