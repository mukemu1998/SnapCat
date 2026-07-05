using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace SnapCat.App;

public partial class MainWindow
{
    private async void RefreshHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await LoadHistoryAsync();
        StatusTextBlock.Text = "历史记录已刷新。";
    }

    private void OpenHistoryDetailButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedHistoryDetail();
    }

    private void OpenImageLocationButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedImageLocation();
    }

    private async void DeleteHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await DeleteSelectedHistoryAsync();
    }

    private async void ClearHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ClearHistoryAsync();
    }

    private void HistoryListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindParent<ListBoxItem>(source) is not null)
        {
            OpenSelectedHistoryDetail();
        }
    }

    private void HistoryListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderHistoryPreview(HistoryListBox.SelectedItem as HistoryListItem);
    }

    private void CopyHistoryPrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HistoryPrimaryTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "已复制左侧预览内容。";
    }

    private void CopyHistorySecondaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(HistorySecondaryTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "已复制右侧预览内容。";
    }

    private async Task LoadHistoryAsync()
    {
        var selectedRecordId = (HistoryListBox.SelectedItem as HistoryListItem)?.Record.RecordId;
        var recent = await _app.HistoryStore.LoadRecentAsync(20);
        var items = recent.Select(record => new HistoryListItem(record)).ToList();
        HistoryListBox.ItemsSource = items;

        if (items.Count == 0)
        {
            HistoryListBox.SelectedItem = null;
            RenderHistoryPreview(null);
            return;
        }

        var selectedItem = items.FirstOrDefault(item => string.Equals(item.Record.RecordId, selectedRecordId, StringComparison.Ordinal))
            ?? items[0];

        HistoryListBox.SelectedItem = selectedItem;
        RenderHistoryPreview(selectedItem);
    }

    private void OpenSelectedHistoryDetail()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var window = BuildHistoryDetailWindow(item.Record)
            ?? BuildUnsupportedHistoryWindow(item.Record);

        window.Owner = this;
        window.ShowDialog();
    }

    private void OpenSelectedImageLocation()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var imagePath = item.Record.ImagePath;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            StatusTextBlock.Text = "该记录没有可用的截图路径。";
            return;
        }

        try
        {
            if (File.Exists(imagePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{imagePath}\"",
                    UseShellExecute = true
                });

                StatusTextBlock.Text = "已打开截图所在位置。";
                return;
            }

            var directory = Path.GetDirectoryName(imagePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });

                StatusTextBlock.Text = "截图文件不存在，已打开所在文件夹。";
                return;
            }

            StatusTextBlock.Text = "截图文件和目录都不存在。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"打开截图位置失败：{ex.Message}";
        }
    }

    private async Task DeleteSelectedHistoryAsync()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var confirm = WpfMessageBox.Show(
            this,
            $"确定删除这条历史记录吗？\n\n{item.Summary}",
            "删除历史记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            StatusTextBlock.Text = "已取消删除。";
            return;
        }

        await _app.HistoryStore.DeleteAsync(item.Record);
        await LoadHistoryAsync();
        StatusTextBlock.Text = "已删除选中的历史记录。";
    }

    private async Task ClearHistoryAsync()
    {
        if (HistoryListBox.Items.Count == 0)
        {
            StatusTextBlock.Text = "当前没有可清空的历史记录。";
            return;
        }

        var confirm = WpfMessageBox.Show(
            this,
            "确定清空全部历史记录吗？此操作不可撤销。",
            "清空历史记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            StatusTextBlock.Text = "已取消清空历史记录。";
            return;
        }

        await _app.HistoryStore.ClearAsync();
        await LoadHistoryAsync();
        StatusTextBlock.Text = "历史记录已清空。";
    }

    private static ResultWindow? BuildHistoryDetailWindow(CaptureTranslationRecord record)
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
                BuildHistoryTranslateStatus(record),
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

    private static ResultWindow BuildUnsupportedHistoryWindow(CaptureTranslationRecord record)
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

    private static string BuildHistoryTranslateStatus(CaptureTranslationRecord record)
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

    private void RenderHistoryPreview(HistoryListItem? item)
    {
        if (item is null)
        {
            HistoryPreviewPanelScrollViewer.Visibility = Visibility.Collapsed;
            HistoryPreviewEmptyTextBlock.Visibility = Visibility.Visible;
            HistoryPreviewTitleTextBlock.Text = string.Empty;
            HistoryPreviewMetaTextBlock.Text = string.Empty;
            HistoryPreviewStatusTextBlock.Text = string.Empty;
            HistoryPrimaryGroupBox.Header = "内容";
            HistoryPrimaryTextBox.Text = string.Empty;
            HistorySecondaryGroupBox.Header = "补充信息";
            HistorySecondaryTextBox.Text = string.Empty;
            ClearHistoryPreviewImage();
            return;
        }

        var preview = BuildHistoryPreviewData(item.Record);

        HistoryPreviewEmptyTextBlock.Visibility = Visibility.Collapsed;
        HistoryPreviewPanelScrollViewer.Visibility = Visibility.Visible;
        HistoryPreviewTitleTextBlock.Text = preview.Title;
        HistoryPreviewMetaTextBlock.Text = preview.Meta;
        HistoryPreviewStatusTextBlock.Text = preview.Status;
        HistoryPrimaryGroupBox.Header = preview.PrimaryHeader;
        HistoryPrimaryTextBox.Text = preview.PrimaryText;
        HistorySecondaryGroupBox.Header = preview.SecondaryHeader;
        HistorySecondaryTextBox.Text = preview.SecondaryText;
        HistoryPreviewPanelScrollViewer.ScrollToHome();
        HistoryPrimaryTextBox.ScrollToHome();
        HistorySecondaryTextBox.ScrollToHome();

        LoadHistoryPreviewImage(item.Record.ImagePath);
    }

    private void LoadHistoryPreviewImage(string? imagePath)
    {
        ClearHistoryPreviewImage();

        if (string.IsNullOrWhiteSpace(imagePath))
        {
            HistoryPreviewUnavailableTextBlock.Text = "这条记录没有关联截图。";
            HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!File.Exists(imagePath))
        {
            HistoryPreviewUnavailableTextBlock.Text = $"截图文件不存在：{imagePath}";
            HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.EndInit();
            bitmap.Freeze();

            HistoryPreviewImage.Source = bitmap;
            HistoryPreviewScrollViewer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            HistoryPreviewUnavailableTextBlock.Text = $"截图预览加载失败：{ex.Message}";
            HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void ClearHistoryPreviewImage()
    {
        HistoryPreviewImage.Source = null;
        HistoryPreviewScrollViewer.Visibility = Visibility.Collapsed;
        HistoryPreviewUnavailableTextBlock.Text = string.Empty;
        HistoryPreviewUnavailableTextBlock.Visibility = Visibility.Collapsed;
    }

    private static HistoryPreviewData BuildHistoryPreviewData(CaptureTranslationRecord record)
    {
        var timestamp = record.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var workflow = FormatWorkflow(record.WorkflowType);
        var meta = $"时间：{timestamp}\n类型：{workflow}\n路径：{FormatSummaryValue(record.ImagePath)}";

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
                BuildHistoryTranslateStatus(record),
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
