using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapCat.App.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private void CleanupTempNowButton_OnClick(object sender, RoutedEventArgs e)
    {
        var deletedTempCount = _app.CapturedImageFileService.CleanupAllTempFiles();

        StatusTextBlock.Text = $"已清理 {deletedTempCount} 个临时文件。正在使用中的文件会自动跳过。";
    }

    private void OpenDefaultCaptureDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDirectory(_app.CapturedImageFileService.GetDefaultDirectoryPath(), "默认截图目录");
    }

    private void OpenTempCaptureDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDirectory(_app.CapturedImageFileService.GetTempDirectoryPath(), "临时文件目录");
    }

    private void RefreshDefaultCapturesButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshDefaultCapturesList();
        StatusTextBlock.Text = "默认保存截图列表已刷新。";
    }

    private void SelectAllDefaultCapturesButton_OnClick(object sender, RoutedEventArgs e)
    {
        DefaultCapturesListBox.SelectAll();
    }

    private void DeleteSelectedDefaultCapturesButton_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedDefaultCaptures();
    }

    private void DefaultCapturesListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var item = FindParent<ListBoxItem>(source);
        if (item is null)
        {
            return;
        }

        if (!item.IsSelected)
        {
            DefaultCapturesListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void DefaultCapturesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeleteSelectedDefaultCapturesButton.IsEnabled = DefaultCapturesListBox.SelectedItems.Count > 0;
    }

    private void OpenSelectedDefaultCaptureLocationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedDefaultCaptureLocation();
    }

    private void DeleteSelectedDefaultCapturesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedDefaultCaptures();
    }

    private void RefreshDefaultCapturesList()
    {
        var directory = _app.CapturedImageFileService.GetDefaultDirectoryPath();
        if (!Directory.Exists(directory))
        {
            DefaultCapturesListBox.ItemsSource = Array.Empty<DefaultCaptureListItem>();
            DeleteSelectedDefaultCapturesButton.IsEnabled = false;
            return;
        }

        var items = Directory
            .EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTime)
            .Select(static path => new DefaultCaptureListItem(path))
            .ToList();
        DefaultCapturesListBox.ItemsSource = items;
        DeleteSelectedDefaultCapturesButton.IsEnabled = false;
    }

    private void OpenSelectedDefaultCaptureLocation()
    {
        if (DefaultCapturesListBox.SelectedItem is not DefaultCaptureListItem item || !File.Exists(item.Path))
        {
            StatusTextBlock.Text = "请先选择要打开位置的截图。";
            return;
        }

        WindowsExplorerService.OpenFileOrContainingDirectory(item.Path);
    }

    private void DeleteSelectedDefaultCaptures()
    {
        var defaultDirectory = Path.GetFullPath(_app.CapturedImageFileService.GetDefaultDirectoryPath())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var selectedItems = DefaultCapturesListBox.SelectedItems
            .OfType<DefaultCaptureListItem>()
            .ToList();

        var deletedCount = 0;
        foreach (var item in selectedItems)
        {
            try
            {
                var fullPath = Path.GetFullPath(item.Path);
                if (fullPath.StartsWith(defaultDirectory, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    deletedCount++;
                }
            }
            catch
            {
                // 文件可能正被外部程序占用，跳过即可。
            }
        }

        RefreshDefaultCapturesList();
        DeleteSelectedDefaultCapturesButton.IsEnabled = false;
        StatusTextBlock.Text = selectedItems.Count == 0
            ? "请先选择要删除的截图。"
            : $"已删除 {deletedCount} 个默认保存截图。";
    }

    private void RenderScreenshotManagementInfo()
    {
        DefaultCaptureDirectoryTextBlock.Text = _app.CapturedImageFileService.GetDefaultDirectoryPath();
        TempCaptureDirectoryTextBlock.Text = _app.CapturedImageFileService.GetTempDirectoryPath();
        RefreshDefaultCapturesList();
    }
}
