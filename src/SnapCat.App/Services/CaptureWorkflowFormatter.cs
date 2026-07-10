namespace SnapCat.App.Services;

internal static class CaptureWorkflowFormatter
{
    public static string ToDisplayName(string value)
    {
        return value switch
        {
            "pin" => "固定到屏幕",
            "ocr" => "OCR 识别",
            "ocr-translate" => "OCR 并翻译",
            "qr" => "二维码识别",
            "save" => "保存截图",
            "annotate" => "框选标注",
            "CaptureAndPin" => "框选+贴图",
            "CaptureAndOcr" => "框选+OCR",
            "CaptureAndTranslate" => "框选+翻译",
            "CaptureAndWaitForAction" => "框选+待执行",
            "CaptureAndSave" => "框选+保存",
            "CaptureAndCopy" => "框选+复制",
            "CaptureAndAnnotate" => "框选+标注",
            "FullScreenCanvasEdit" => "全屏画布",
            _ => "未知操作"
        };
    }
}
