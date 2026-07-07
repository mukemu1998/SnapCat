namespace SnapCat.Core.Models;

public sealed class CaptureTranslationRecord
{
    public string RecordId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string WorkflowType { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public string TranslatedText { get; set; } = string.Empty;

    public string QrCodeText { get; set; } = string.Empty;

    public string OcrError { get; set; } = string.Empty;

    public string OcrDebugInfo { get; set; } = string.Empty;

    public string TranslationError { get; set; } = string.Empty;
}
