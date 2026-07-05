using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SnapCat.App.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private void RefreshPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "贴图列表已刷新。";
    }

    private void SelectAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        PinnedImagesListBox.SelectAll();
    }

    private void DeleteSelectedPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var ids = PinnedImagesListBox.SelectedItems
            .OfType<PinnedImageListItem>()
            .Select(item => item.Id)
            .ToList();

        _app.PinnedWindowRegistryService.CloseSnapshots(ids);
        RefreshPinnedImagesList();
        StatusTextBlock.Text = ids.Count == 0 ? "请先选择要删除的贴图。" : $"已删除 {ids.Count} 个贴图。";
    }

    private void DeleteAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.CloseAllWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已删除全部贴图。";
    }

    private void ShowAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowAllPinnedImages();
    }

    private void HideAllPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideAllPinnedImages();
    }

    private void ShowUngroupedPinnedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowUngroupedPinnedImages();
    }

    private void ShowPinnedGroupOneButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowPinnedGroup(PinnedWindowRegistryService.GroupOneName);
    }

    private void ShowPinnedGroupTwoButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowPinnedGroup(PinnedWindowRegistryService.GroupTwoName);
    }

    private void ShowPinnedGroupThreeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowPinnedGroup(PinnedWindowRegistryService.GroupThreeName);
    }

    private void ShowPinnedGroup(string groupName)
    {
        _app.PinnedWindowRegistryService.ShowGroup(groupName);
        RefreshPinnedImagesList();
        StatusTextBlock.Text = $"已显示{groupName}。";
    }

    private void ShowAllPinnedImages()
    {
        _app.PinnedWindowRegistryService.ShowAllWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已显示全部贴图。";
    }

    private void HideAllPinnedImages()
    {
        _app.PinnedWindowRegistryService.HideAllWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已隐藏全部贴图。";
    }

    private void ShowUngroupedPinnedImages()
    {
        _app.PinnedWindowRegistryService.ShowUngroupedWindows();
        RefreshPinnedImagesList();
        StatusTextBlock.Text = "已显示未成组贴图。";
    }

    private void PinnedImagesListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
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
            PinnedImagesListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void AssignPinnedImagesToUngroupedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.UngroupedGroupName);
    }

    private void AssignPinnedImagesToGroupOneMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.GroupOneName);
    }

    private void AssignPinnedImagesToGroupTwoMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.GroupTwoName);
    }

    private void AssignPinnedImagesToGroupThreeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        AssignSelectedPinnedImagesToGroup(PinnedWindowRegistryService.GroupThreeName);
    }

    private void DeleteSelectedPinnedImagesMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        DeleteSelectedPinnedImagesButton_OnClick(sender, e);
    }

    private void AssignSelectedPinnedImagesToGroup(string groupName)
    {
        var ids = GetSelectedPinnedImageIds();
        _app.PinnedWindowRegistryService.SetSnapshotsGroup(ids, groupName);
        RefreshPinnedImagesList();
        StatusTextBlock.Text = ids.Count == 0
            ? "请先选择要指定分组的贴图。"
            : $"已更新 {ids.Count} 个贴图的分组。";
    }

    private void RefreshPinnedImagesList()
    {
        PinnedImagesListBox.ItemsSource = _app.PinnedWindowRegistryService
            .GetActiveSnapshots()
            .Select(static snapshot => new PinnedImageListItem(snapshot))
            .ToList();
    }

    private List<string> GetSelectedPinnedImageIds()
    {
        return PinnedImagesListBox.SelectedItems
            .OfType<PinnedImageListItem>()
            .Select(item => item.Id)
            .ToList();
    }
}
