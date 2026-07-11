using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnapCat.App.Services;
using SnapCat.App.Windows;
using Clipboard = System.Windows.Clipboard;

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
        DeleteHistoryButton.IsEnabled = HistoryListBox.SelectedItem is not null;
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
            DeleteHistoryButton.IsEnabled = false;
            ClearHistoryButton.IsEnabled = false;
            return;
        }

        var selectedItem = items.FirstOrDefault(item => string.Equals(item.Record.RecordId, selectedRecordId, StringComparison.Ordinal))
            ?? items[0];

        HistoryListBox.SelectedItem = selectedItem;
        RenderHistoryPreview(selectedItem);
        DeleteHistoryButton.IsEnabled = true;
        ClearHistoryButton.IsEnabled = true;
    }

    private void OpenSelectedHistoryDetail()
    {
        if (HistoryListBox.SelectedItem is not HistoryListItem item)
        {
            StatusTextBlock.Text = "请先在历史记录中选择一项。";
            return;
        }

        var window = HistoryDetailWindowFactory.Create(item.Record);
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
            var result = WindowsExplorerService.OpenFileOrContainingDirectory(imagePath);
            StatusTextBlock.Text = result switch
            {
                ExplorerOpenResult.FileSelected => "已打开截图所在位置。",
                ExplorerOpenResult.DirectoryOpened => "截图文件不存在，已打开所在文件夹。",
                _ => "截图文件和目录都不存在。"
            };
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

        var confirmed = ConfirmDialogWindow.Confirm(
            this,
            "删除历史记录",
            $"确定要删除这条历史记录吗？\n\n{item.Summary}",
            "删除");

        if (!confirmed)
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

        var confirmed = ConfirmDialogWindow.Confirm(
            this,
            "清空历史记录",
            "确定要清空全部历史记录吗？此操作不可撤销。",
            "清空");

        if (!confirmed)
        {
            StatusTextBlock.Text = "已取消清空历史记录。";
            return;
        }

        await _app.HistoryStore.ClearAsync();
        await LoadHistoryAsync();
        StatusTextBlock.Text = "历史记录已清空。";
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

        var preview = HistoryPreviewBuilder.Build(item.Record);

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

}
