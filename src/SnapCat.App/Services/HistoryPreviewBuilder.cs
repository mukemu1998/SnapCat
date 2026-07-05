using SnapCat.Core.Models;

namespace SnapCat.App.Services;

internal static class HistoryPreviewBuilder
{
    public static HistoryPreviewData Build(CaptureTranslationRecord record)
    {
        var timestamp = record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = SettingsSummaryFormatter.FormatWorkflow(record.WorkflowType);
        var meta = $"时间：{timestamp}\n类型：{workflow}\n路径：{SettingsSummaryFormatter.FormatSummaryValue(record.ImagePath)}";

        return record.WorkflowType switch
        {
            "ocr" => new HistoryPreviewData(
                "OCR 识别记录",
                meta,
                string.IsNullOrWhiteSpace(record.OcrError) ? "OCR 识别已完成。" : $"OCR 失败：{record.OcrError}",
                "OCR 文本",
                PickValue(record.SourceText, record.OcrError),
                "调试信息",
                PickValue(record.OcrDebugInfo, "当前没有额外调试信息。")),
            "ocr-translate" => new HistoryPreviewData(
                "OCR 并翻译记录",
                meta,
                BuildTranslateStatus(record),
                "原文",
                PickValue(record.SourceText, record.OcrError),
                "译文",
                PickValue(record.TranslatedText, record.TranslationError)),
            "qr" => new HistoryPreviewData(
                "二维码识别记录",
                meta,
                string.IsNullOrWhiteSpace(record.OcrError) ? "二维码识别已完成。" : $"二维码识别失败：{record.OcrError}",
                "二维码内容",
                PickValue(record.QrCodeText, record.OcrError),
                "补充信息",
                "这条记录来自二维码识别流程。"),
            "pin" => new HistoryPreviewData(
                "固定到屏幕记录",
                meta,
                "这条记录表示该截图曾被固定到屏幕。",
                "截图路径",
                PickValue(record.ImagePath, "没有可用的截图路径。"),
                "补充信息",
                "固定到屏幕不会额外产生 OCR 或翻译结果。"),
            _ => new HistoryPreviewData(
                "历史记录",
                meta,
                "该记录类型暂未定义专用预览结构。",
                "主要内容",
                PickMainHistoryContent(record),
                "补充信息",
                PickValue(record.OcrDebugInfo, "暂无补充信息。"))
        };
    }

    public static string BuildTranslateStatus(CaptureTranslationRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.OcrError) && string.IsNullOrWhiteSpace(record.TranslationError))
        {
            return "OCR 和翻译已完成。";
        }

        if (!string.IsNullOrWhiteSpace(record.OcrError))
        {
            return $"OCR 失败：{record.OcrError}";
        }

        return $"翻译失败：{record.TranslationError}";
    }

    private static string PickValue(string? value, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.IsNullOrWhiteSpace(fallback) ? "暂无内容。" : fallback;
    }

    private static string PickMainHistoryContent(CaptureTranslationRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.QrCodeText))
        {
            return record.QrCodeText;
        }

        if (!string.IsNullOrWhiteSpace(record.TranslatedText))
        {
            return record.TranslatedText;
        }

        if (!string.IsNullOrWhiteSpace(record.SourceText))
        {
            return record.SourceText;
        }

        if (!string.IsNullOrWhiteSpace(record.OcrError))
        {
            return record.OcrError;
        }

        return PickValue(record.ImagePath, "暂无内容。");
    }
}
