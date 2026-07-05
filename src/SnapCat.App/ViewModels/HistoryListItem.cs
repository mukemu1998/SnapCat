using SnapCat.App.Services;
using SnapCat.Core.Models;

namespace SnapCat.App;

internal sealed record HistoryListItem(CaptureTranslationRecord Record)
{
    public string Summary
    {
        get
        {
            var timestamp = Record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            var workflow = CaptureWorkflowFormatter.ToDisplayName(Record.WorkflowType);
            var mainContent = PickMainContent(Record);
            return $"{timestamp} | {workflow} | {mainContent}";
        }
    }

    private static string PickMainContent(CaptureTranslationRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.QrCodeText))
        {
            return $"二维码：{TrimForList(record.QrCodeText)}";
        }

        if (!string.IsNullOrWhiteSpace(record.TranslatedText))
        {
            return $"译文：{TrimForList(record.TranslatedText)}";
        }

        if (!string.IsNullOrWhiteSpace(record.SourceText))
        {
            return $"文本：{TrimForList(record.SourceText)}";
        }

        if (!string.IsNullOrWhiteSpace(record.OcrError))
        {
            return $"错误：{TrimForList(record.OcrError)}";
        }

        return $"图片：{TrimForList(record.ImagePath)}";
    }

    private static string TrimForList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var singleLine = value.ReplaceLineEndings(" ").Trim();
        const int maxLength = 42;
        return singleLine.Length <= maxLength
            ? singleLine
            : $"{singleLine[..maxLength]}...";
    }
}
