using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class SmartOcrService : IOcrService
{
    private readonly WindowsMediaOcrService _windowsMediaOcrService;
    private readonly EnhancedTesseractOcrService _enhancedTesseractOcrService;
    private readonly TesseractCliOcrService _tesseractCliOcrService;

    public SmartOcrService(
        WindowsMediaOcrService windowsMediaOcrService,
        EnhancedTesseractOcrService enhancedTesseractOcrService,
        TesseractCliOcrService tesseractCliOcrService)
    {
        _windowsMediaOcrService = windowsMediaOcrService;
        _enhancedTesseractOcrService = enhancedTesseractOcrService;
        _tesseractCliOcrService = tesseractCliOcrService;
    }

    public async Task<OcrResult> RecognizeAsync(
        string imagePath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(settings.OcrEngine, "windows-media-ocr", StringComparison.Ordinal))
        {
            return await _windowsMediaOcrService.RecognizeAsync(imagePath, settings, cancellationToken);
        }

        var primaryResult = string.Equals(settings.OcrEngine, "tesseract-cli", StringComparison.Ordinal)
            ? await _tesseractCliOcrService.RecognizeAsync(imagePath, settings, cancellationToken)
            : await _enhancedTesseractOcrService.RecognizeAsync(imagePath, settings, cancellationToken);

        if (primaryResult.Success && !string.IsNullOrWhiteSpace(primaryResult.Text))
        {
            return primaryResult;
        }

        var fallbackResult = await _windowsMediaOcrService.RecognizeAsync(imagePath, settings, cancellationToken);
        if (fallbackResult.Success && !string.IsNullOrWhiteSpace(fallbackResult.Text))
        {
            return OcrResult.FromText(
                fallbackResult.Text,
                fallbackResult.EngineName,
                $"{primaryResult.DebugSummary}{Environment.NewLine}{Environment.NewLine}已自动回退到系统内置 OCR。{Environment.NewLine}{fallbackResult.DebugSummary}".Trim());
        }

        return OcrResult.FromError(
            $"{primaryResult.ErrorMessage}；系统内置 OCR 也未成功：{fallbackResult.ErrorMessage}",
            "smart-local-ocr",
            $"{primaryResult.DebugSummary}{Environment.NewLine}{Environment.NewLine}{fallbackResult.DebugSummary}".Trim());
    }
}
