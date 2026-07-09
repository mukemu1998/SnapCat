namespace SnapCat.Core.Models;

public enum CaptureActionKind
{
    PinToScreen,
    OcrOnly,
    OcrAndTranslate,
    QrCode,
    CanvasEdit,
    CopyImage,
    Save,
    SaveAs,
    Cancel
}
