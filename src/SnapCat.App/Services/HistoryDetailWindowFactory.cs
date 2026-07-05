using SnapCat.App.Windows;
using SnapCat.Core.Models;

namespace SnapCat.App.Services;

internal static class HistoryDetailWindowFactory
{
    public static ResultWindow Create(CaptureTranslationRecord record)
    {
        return CreateTypedWindow(record) ?? CreateUnsupportedWindow(record);
    }

    private static ResultWindow? CreateTypedWindow(CaptureTranslationRecord record)
    {
        return record.WorkflowType switch
        {
            "ocr" => new ResultWindow(
                "历史详情 - OCR 识别",
                string.IsNullOrWhiteSpace(record.OcrError) ? "OCR 识别已完成。" : $"OCR 失败：{record.OcrError}",
                "OCR 文本",
                string.IsNullOrWhiteSpace(record.SourceText) ? record.OcrError : record.SourceText,
                "截图路径",
                record.ImagePath,
                record.OcrDebugInfo,
                imagePath: record.ImagePath),
            "ocr-translate" => new ResultWindow(
                "历史详情 - OCR 并翻译",
                HistoryPreviewBuilder.BuildTranslateStatus(record),
                "原文",
                string.IsNullOrWhiteSpace(record.SourceText) ? record.OcrError : record.SourceText,
                "译文",
                string.IsNullOrWhiteSpace(record.TranslatedText) ? record.TranslationError : record.TranslatedText,
                record.OcrDebugInfo,
                imagePath: record.ImagePath),
            "qr" => new ResultWindow(
                "历史详情 - 二维码识别",
                string.IsNullOrWhiteSpace(record.OcrError) ? "二维码识别已完成。" : $"二维码识别失败：{record.OcrError}",
                "二维码内容",
                string.IsNullOrWhiteSpace(record.QrCodeText) ? record.OcrError : record.QrCodeText,
                "截图路径",
                record.ImagePath,
                imagePath: record.ImagePath),
            "pin" => new ResultWindow(
                "历史详情 - 固定到屏幕",
                "这条记录表示该截图曾被固定到屏幕。",
                "截图路径",
                record.ImagePath,
                "备注",
                "固定到屏幕不会额外产生 OCR 或翻译结果。",
                imagePath: record.ImagePath),
            "save" => new ResultWindow(
                "历史详情 - 保存截图",
                "这条记录表示该截图已保存到默认目录。",
                "截图路径",
                record.ImagePath,
                "备注",
                "保存截图不会额外产生 OCR 或翻译结果。",
                imagePath: record.ImagePath),
            _ => null
        };
    }

    private static ResultWindow CreateUnsupportedWindow(CaptureTranslationRecord record)
    {
        return new ResultWindow(
            "历史详情",
            "该记录类型暂未定义专用详情视图。",
            "记录类型",
            record.WorkflowType,
            "截图路径",
            record.ImagePath,
            record.OcrDebugInfo,
            imagePath: record.ImagePath);
    }
}
