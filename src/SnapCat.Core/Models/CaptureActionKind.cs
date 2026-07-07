namespace SnapCat.Core.Models;

public enum CaptureActionKind
{
    PinToScreen,
    OcrOnly,
    OcrAndTranslate,
    QrCode,
    CopyImage,
    Save,
    SaveAs,
    Cancel
}
