namespace SnapCat.Core.Models;

public enum CaptureActionKind
{
    PinToScreen,
    OcrOnly,
    OcrAndTranslate,
    VisualPromptAnalysis,
    QrCode,
    CanvasEdit,
    CopyImage,
    Save,
    SaveAs,
    Cancel
}
