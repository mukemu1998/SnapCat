namespace SnapCat.Core.Models;

public enum CaptureWorkflowKind
{
    CaptureAndPin,
    CaptureAndOcr,
    CaptureAndTranslate,
    CaptureAndWaitForAction,
    CaptureAndSave,
    CaptureAndCopy,
    CaptureAndAnnotate,
    CaptureAndVisualPrompt,
    FullScreenCanvasEdit
}
