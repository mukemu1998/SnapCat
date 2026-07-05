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
            _ => "未知操作"
        };
    }
}
