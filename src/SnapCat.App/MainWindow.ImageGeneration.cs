using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SnapCat.App.Services;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapCat.App;

public partial class MainWindow
{
    private readonly List<ImageGenerationProfile> _imageGenerationProfiles = [];
    private readonly HashSet<string> _imageGenerationModelDiscoveryAttempted = new(StringComparer.Ordinal);
    private string _selectedImageGenerationProfileId = string.Empty;
    private bool _isApplyingImageGenerationProfileState;
    private CancellationTokenSource? _imageGenerationCancellation;

    public ObservableCollection<string> AvailableImageGenerationModelNames { get; } = [];

    private void LoadImageGenerationProfiles(AppSettings settings)
    {
        _isApplyingImageGenerationProfileState = true;
        _imageGenerationProfiles.Clear();
        _imageGenerationProfiles.AddRange(ImageGenerationProfile.CloneAll(settings.ImageGenerationProfiles));
        _imageGenerationModelDiscoveryAttempted.Clear();
        RebuildAvailableImageGenerationModels();
        _selectedImageGenerationProfileId = settings.SelectedImageGenerationProfileId;
        EnsureDefaultImageGenerationProfile();
        RefreshImageGenerationProfileEditor();
        _isApplyingImageGenerationProfileState = false;
    }

    private void PersistImageGenerationProfileEditor()
    {
        for (var index = 0; index < _imageGenerationProfiles.Count; index++)
        {
            _imageGenerationProfiles[index].Normalize(index);
        }

        EnsureDefaultImageGenerationProfile();
    }

    private void RefreshImageGenerationProfileEditor()
    {
        if (ImageGenerationProfileExpandersItemsControl is null)
        {
            return;
        }

        ImageGenerationProfileExpandersItemsControl.ItemsSource = null;
        ImageGenerationProfileExpandersItemsControl.ItemsSource = _imageGenerationProfiles;

        var selected = GetSelectedImageGenerationProfile();
        EmptyImageGenerationProfileTextBlock.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;
        DeleteImageGenerationProfileButton.IsEnabled = selected is not null;
        var defaultProfile = GetDefaultImageGenerationProfile();
        ImageGenerationDefaultProfileTextBlock.Text = defaultProfile is null
            ? string.Empty
            : $"默认配置：{defaultProfile.Name}（下方单图文生图会使用它）";

        if (defaultProfile is not null)
        {
            ImageGenerationWidthTextBox.Text = defaultProfile.DefaultWidth.ToString();
            ImageGenerationHeightTextBox.Text = defaultProfile.DefaultHeight.ToString();
            ImageGenerationStepsTextBox.Text = defaultProfile.DefaultSteps.ToString();
            ImageGenerationCfgScaleTextBox.Text = defaultProfile.DefaultCfgScale.ToString("0.##", CultureInfo.InvariantCulture);
        }

        UpdateImageGenerationActionState();
    }

    private ImageGenerationProfile? GetSelectedImageGenerationProfile()
    {
        return _imageGenerationProfiles.FirstOrDefault(profile =>
                   string.Equals(profile.Id, _selectedImageGenerationProfileId, StringComparison.Ordinal))
               ?? _imageGenerationProfiles.FirstOrDefault();
    }

    private ImageGenerationProfile? GetDefaultImageGenerationProfile()
    {
        return _imageGenerationProfiles.FirstOrDefault(profile =>
                   profile.IsEnabled && profile.IsDefault)
               ?? _imageGenerationProfiles.FirstOrDefault(profile => profile.IsEnabled)
               ?? _imageGenerationProfiles.FirstOrDefault();
    }

    private void EnsureDefaultImageGenerationProfile()
    {
        var selected = _imageGenerationProfiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _selectedImageGenerationProfileId, StringComparison.Ordinal));
        var defaultProfile = _imageGenerationProfiles.FirstOrDefault(profile => profile.IsEnabled && profile.IsDefault)
            ?? (selected?.IsEnabled == true ? selected : _imageGenerationProfiles.FirstOrDefault(profile => profile.IsEnabled));

        _selectedImageGenerationProfileId = defaultProfile?.Id ?? string.Empty;
        foreach (var profile in _imageGenerationProfiles)
        {
            profile.IsDefault = string.Equals(profile.Id, _selectedImageGenerationProfileId, StringComparison.Ordinal);
        }
    }

    private void AddImageGenerationProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        PersistImageGenerationProfileEditor();
        var profile = new ImageGenerationProfile
        {
            Name = $"ComfyUI 配置 {_imageGenerationProfiles.Count + 1}",
            BaseUrl = "http://127.0.0.1:8188",
            IsDefault = _imageGenerationProfiles.Count == 0
        };
        _imageGenerationProfiles.Add(profile);
        RebuildAvailableImageGenerationModels();
        _selectedImageGenerationProfileId = profile.Id;
        _isApplyingImageGenerationProfileState = true;
        RefreshImageGenerationProfileEditor();
        _isApplyingImageGenerationProfileState = false;
        MarkSettingsDirty();
        SetImageGenerationProfileStatus($"已添加图像生成配置：{profile.Name}");
    }

    private void DeleteImageGenerationProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedImageGenerationProfile();
        if (profile is null)
        {
            return;
        }

        _imageGenerationProfiles.Remove(profile);
        RebuildAvailableImageGenerationModels();
        _selectedImageGenerationProfileId = _imageGenerationProfiles.FirstOrDefault()?.Id ?? string.Empty;
        _isApplyingImageGenerationProfileState = true;
        EnsureDefaultImageGenerationProfile();
        RefreshImageGenerationProfileEditor();
        _isApplyingImageGenerationProfileState = false;
        MarkSettingsDirty();
        SetImageGenerationProfileStatus($"已删除图像生成配置：{profile.Name}");
    }

    private void ImageGenerationProfileExpander_OnExpanded(object sender, RoutedEventArgs e)
    {
        if (_isApplyingImageGenerationProfileState
            || sender is not Expander { DataContext: ImageGenerationProfile profile })
        {
            return;
        }

        _selectedImageGenerationProfileId = profile.Id;
        DeleteImageGenerationProfileButton.IsEnabled = true;
    }

    private void ImageGenerationProfileEnabled_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_isApplyingImageGenerationProfileState
            || sender is not WpfCheckBox { DataContext: ImageGenerationProfile profile })
        {
            return;
        }

        _selectedImageGenerationProfileId = profile.Id;
        EnsureDefaultImageGenerationProfile();
        RefreshImageGenerationProfileEditor();
        MarkSettingsDirty();
    }

    private void SetDefaultImageGenerationProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element
            || FindParent<Expander>(element) is not { DataContext: ImageGenerationProfile profile })
        {
            return;
        }

        if (!profile.IsEnabled)
        {
            SetImageGenerationProfileStatus("请先勾选“允许用于生图”，再设为默认配置。");
            return;
        }

        _selectedImageGenerationProfileId = profile.Id;
        EnsureDefaultImageGenerationProfile();
        RefreshImageGenerationProfileEditor();
        MarkSettingsDirty();
        SetImageGenerationProfileStatus($"已将“{profile.Name}”设为默认图像生成配置。");
    }

    private void ImageGenerationProfileTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingImageGenerationProfileState
            || sender is not WpfTextBox { DataContext: ImageGenerationProfile profile })
        {
            return;
        }

        _selectedImageGenerationProfileId = profile.Id;
        UpdateImageGenerationActionState();
        MarkSettingsDirty();
    }

    private void ImageGenerationParameters_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isApplyingImageGenerationProfileState
            || ImageGenerationWidthTextBox is null
            || ImageGenerationHeightTextBox is null
            || ImageGenerationStepsTextBox is null
            || ImageGenerationCfgScaleTextBox is null)
        {
            return;
        }

        var profile = GetDefaultImageGenerationProfile();
        if (profile is null)
        {
            UpdateImageGenerationActionState();
            return;
        }

        if (int.TryParse(ImageGenerationWidthTextBox.Text, out var width))
        {
            profile.DefaultWidth = width;
        }

        if (int.TryParse(ImageGenerationHeightTextBox.Text, out var height))
        {
            profile.DefaultHeight = height;
        }

        if (int.TryParse(ImageGenerationStepsTextBox.Text, out var steps))
        {
            profile.DefaultSteps = steps;
        }

        if (TryParseCfgScale(ImageGenerationCfgScaleTextBox.Text, out var cfgScale))
        {
            profile.DefaultCfgScale = cfgScale;
        }

        UpdateImageGenerationActionState();
        MarkSettingsDirty();
    }

    private void ImageGenerationProfileModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingImageGenerationProfileState
            || sender is not WpfComboBox { DataContext: ImageGenerationProfile profile })
        {
            return;
        }

        _selectedImageGenerationProfileId = profile.Id;
        MarkSettingsDirty();
    }

    private async void TestImageGenerationProfileConnectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { DataContext: ImageGenerationProfile profile } button)
        {
            return;
        }

        button.IsEnabled = false;
        SetImageGenerationProfileStatus($"正在检测“{profile.Name}”的 ComfyUI 连接...");
        try
        {
            var result = await _app.ImageGenerationService.TestConnectionAsync(profile);
            SetImageGenerationProfileStatus(result.Message);
        }
        catch (Exception exception)
        {
            SetImageGenerationProfileStatus($"检测 ComfyUI 连接失败：{exception.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void LoadImageGenerationModelsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { DataContext: ImageGenerationProfile profile } button)
        {
            return;
        }

        button.IsEnabled = false;
        SetImageGenerationProfileStatus($"正在读取“{profile.Name}”已安装的 Checkpoint...");
        try
        {
            var models = await _app.ImageGenerationService.GetCheckpointModelsAsync(profile);
            _imageGenerationModelDiscoveryAttempted.Add(profile.Id);
            MergeAvailableImageGenerationModels(models);

            SetImageGenerationProfileStatus(models.Count == 0
                ? "未读取到 Checkpoint。请确认 ComfyUI 已启动且至少安装了一个基础模型。"
                : $"已读取 {models.Count} 个 Checkpoint。请选择一个作为当前配置的基础模型。");
        }
        catch (Exception exception)
        {
            SetImageGenerationProfileStatus($"读取 ComfyUI 模型失败：{exception.Message}");
        }
        finally
        {
            button.IsEnabled = true;
            UpdateImageGenerationActionState();
        }
    }

    private async Task RefreshImageGenerationModelsAsync()
    {
        var profile = GetDefaultImageGenerationProfile();
        if (profile is null
            || !profile.IsEnabled
            || _imageGenerationModelDiscoveryAttempted.Contains(profile.Id))
        {
            return;
        }

        _imageGenerationModelDiscoveryAttempted.Add(profile.Id);
        try
        {
            var models = await _app.ImageGenerationService.GetCheckpointModelsAsync(profile);
            MergeAvailableImageGenerationModels(models);
        }
        catch
        {
            // Connection feedback belongs to an explicit user action, not section navigation.
        }
        finally
        {
            UpdateImageGenerationActionState();
        }
    }

    private async void RunImageGenerationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var profile = GetDefaultImageGenerationProfile();
        if (profile is null || !profile.IsEnabled)
        {
            ImageGenerationStatusTextBlock.Text = "请先添加并启用一个 ComfyUI 图像生成配置。";
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultCheckpoint))
        {
            ImageGenerationStatusTextBlock.Text = "请先读取并选择 ComfyUI 已安装的基础模型。";
            return;
        }

        if (!int.TryParse(ImageGenerationWidthTextBox.Text, out var width)
            || !int.TryParse(ImageGenerationHeightTextBox.Text, out var height)
            || !int.TryParse(ImageGenerationStepsTextBox.Text, out var steps)
            || !TryParseCfgScale(ImageGenerationCfgScaleTextBox.Text, out var cfgScale))
        {
            ImageGenerationStatusTextBlock.Text = "宽、高、步数和 CFG 必须为有效数字。";
            return;
        }

        if (width is < 256 or > 4096
            || height is < 256 or > 4096
            || steps is < 1 or > 150
            || cfgScale is < 1d or > 30d)
        {
            ImageGenerationStatusTextBlock.Text = "宽高范围为 256-4096，步数范围为 1-150，CFG 范围为 1-30。";
            return;
        }

        if (string.IsNullOrWhiteSpace(ImageGenerationPromptTextBox.Text))
        {
            ImageGenerationStatusTextBlock.Text = "请先输入用于生图的提示词。";
            return;
        }

        profile.DefaultWidth = width;
        profile.DefaultHeight = height;
        profile.DefaultSteps = steps;
        profile.DefaultCfgScale = cfgScale;
        MarkSettingsDirty();

        _imageGenerationCancellation?.Dispose();
        _imageGenerationCancellation = new CancellationTokenSource();
        UpdateImageGenerationActionState();
        ImageGenerationStatusTextBlock.Text = "ComfyUI 正在生成单张图片，请勿关闭 SnapCat。";
        try
        {
            var result = await _app.ImageGenerationService.GenerateAsync(new ImageGenerationRequest
            {
                Prompt = ImageGenerationPromptTextBox.Text,
                NegativePrompt = ImageGenerationNegativePromptTextBox.Text,
                Checkpoint = profile.DefaultCheckpoint,
                Width = width,
                Height = height,
                Steps = steps,
                CfgScale = cfgScale,
                OutputCount = 1
            }, profile, _imageGenerationCancellation.Token);

            if (!result.Success || result.Outputs.Count == 0)
            {
                ImageGenerationStatusTextBlock.Text = result.ErrorMessage;
                return;
            }

            var output = result.Outputs[0];
            var path = await _app.GeneratedImageFileService.SaveAsync(
                output.FileName,
                output.Content);

            using var stream = new MemoryStream(output.Content);
            var preview = new BitmapImage();
            preview.BeginInit();
            preview.CacheOption = BitmapCacheOption.OnLoad;
            preview.StreamSource = stream;
            preview.EndInit();
            preview.Freeze();
            ImageGenerationPreviewImage.Source = preview;
            ImageGenerationStatusTextBlock.Text = $"生成完成，已保存到：{path}";
            RefreshGeneratedImagesList();
        }
        catch (Exception exception)
        {
            ImageGenerationStatusTextBlock.Text = $"处理生图结果失败：{exception.Message}";
        }
        finally
        {
            _imageGenerationCancellation.Dispose();
            _imageGenerationCancellation = null;
            UpdateImageGenerationActionState();
        }
    }

    private void CancelImageGenerationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_imageGenerationCancellation is null)
        {
            return;
        }

        CancelImageGenerationButton.IsEnabled = false;
        ImageGenerationStatusTextBlock.Text = "正在停止 SnapCat 对当前任务的等待...";
        _imageGenerationCancellation.Cancel();
    }

    private void RebuildAvailableImageGenerationModels()
    {
        AvailableImageGenerationModelNames.Clear();
        MergeAvailableImageGenerationModels(_imageGenerationProfiles.Select(profile => profile.DefaultCheckpoint));
    }

    private void MergeAvailableImageGenerationModels(IEnumerable<string> models)
    {
        foreach (var model in models
                     .Where(static model => !string.IsNullOrWhiteSpace(model))
                     .Select(static model => model.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static model => model, StringComparer.OrdinalIgnoreCase))
        {
            if (!AvailableImageGenerationModelNames.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                AvailableImageGenerationModelNames.Add(model);
            }
        }
    }

    private void UpdateImageGenerationActionState()
    {
        if (RunImageGenerationButton is null || CancelImageGenerationButton is null)
        {
            return;
        }

        var profile = GetDefaultImageGenerationProfile();
        var isRunning = _imageGenerationCancellation is not null;
        RunImageGenerationButton.IsEnabled = !isRunning
                                             && profile?.IsEnabled == true
                                             && !string.IsNullOrWhiteSpace(profile.DefaultCheckpoint);
        CancelImageGenerationButton.IsEnabled = isRunning
                                                && _imageGenerationCancellation?.IsCancellationRequested == false;
    }

    private static bool TryParseCfgScale(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    private void SetImageGenerationProfileStatus(string message)
    {
        if (ImageGenerationProfileStatusBorder is null || ImageGenerationProfileStatusTextBlock is null)
        {
            return;
        }

        ImageGenerationProfileStatusTextBlock.Text = message;
        ImageGenerationProfileStatusBorder.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OpenGeneratedImagesDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowsExplorerService.OpenDirectory(
            _app.GeneratedImageFileService.GetDirectoryPath(),
            createIfMissing: true);
        SetGeneratedImagesStatus("已打开本地生成图片目录。");
    }

    private void RefreshGeneratedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshGeneratedImagesList();
        SetGeneratedImagesStatus("生成图片列表已刷新。");
    }

    private void ToggleSelectAllGeneratedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (GeneratedImagesListBox.Items.Count > 0
            && GeneratedImagesListBox.SelectedItems.Count == GeneratedImagesListBox.Items.Count)
        {
            GeneratedImagesListBox.UnselectAll();
            return;
        }

        GeneratedImagesListBox.SelectAll();
    }

    private void DeleteSelectedGeneratedImagesButton_OnClick(object sender, RoutedEventArgs e) =>
        DeleteSelectedGeneratedImages();

    private void DeleteAllGeneratedImagesButton_OnClick(object sender, RoutedEventArgs e)
    {
        var itemCount = GeneratedImagesListBox.Items.Count;
        if (itemCount == 0)
        {
            SetGeneratedImagesStatus("当前没有可删除的生成图片。");
            return;
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "删除全部生成图片",
                $"确定要永久删除本地 generated 目录中的 {itemCount} 张图片吗？此操作不可撤销。",
                "全部删除"))
        {
            return;
        }

        var deletedCount = _app.GeneratedImageFileService.DeleteAll();
        RefreshGeneratedImagesList();
        SetGeneratedImagesStatus($"已删除 {deletedCount} 张生成图片。");
    }

    private void GeneratedImagesListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedCount = GeneratedImagesListBox.SelectedItems.Count;
        DeleteSelectedGeneratedImagesButton.IsEnabled = selectedCount > 0;
        ToggleSelectAllGeneratedImagesButton.Content = GeneratedImagesListBox.Items.Count > 0
                                                      && selectedCount == GeneratedImagesListBox.Items.Count
            ? "取消全选"
            : "全选";
    }

    private void GeneratedImagesListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || FindParent<ListBoxItem>(source) is not { } item)
        {
            return;
        }

        if (!item.IsSelected)
        {
            GeneratedImagesListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void OpenSelectedGeneratedImageLocationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GeneratedImagesListBox.SelectedItem is not GeneratedImageListItem item || !File.Exists(item.Path))
        {
            SetGeneratedImagesStatus("请先选择要打开位置的生成图片。");
            return;
        }

        WindowsExplorerService.OpenFileOrContainingDirectory(item.Path);
    }

    private void DeleteSelectedGeneratedImagesMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        DeleteSelectedGeneratedImages();

    private void RefreshGeneratedImagesList()
    {
        var items = _app.GeneratedImageFileService
            .GetImagePaths()
            .Select(static path => new GeneratedImageListItem(path))
            .ToList();
        GeneratedImagesListBox.ItemsSource = items;
        GeneratedImagesSummaryTextBlock.Text = items.Count == 0
            ? $"暂无生成图片 | {_app.GeneratedImageFileService.GetDirectoryPath()}"
            : $"共 {items.Count} 张生成图片 | {_app.GeneratedImageFileService.GetDirectoryPath()}";
        EmptyGeneratedImagesTextBlock.Visibility = items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        DeleteSelectedGeneratedImagesButton.IsEnabled = false;
        DeleteAllGeneratedImagesButton.IsEnabled = items.Count > 0;
        ToggleSelectAllGeneratedImagesButton.IsEnabled = items.Count > 0;
        ToggleSelectAllGeneratedImagesButton.Content = "全选";
    }

    private void DeleteSelectedGeneratedImages()
    {
        var selectedItems = GeneratedImagesListBox.SelectedItems
            .OfType<GeneratedImageListItem>()
            .ToList();
        if (selectedItems.Count == 0)
        {
            SetGeneratedImagesStatus("请先选择要删除的生成图片。");
            return;
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "删除生成图片",
                $"确定要永久删除选中的 {selectedItems.Count} 张生成图片吗？此操作不可撤销。",
                "删除"))
        {
            return;
        }

        var deletedCount = _app.GeneratedImageFileService.DeleteFiles(selectedItems.Select(static item => item.Path));
        RefreshGeneratedImagesList();
        SetGeneratedImagesStatus($"已删除 {deletedCount} 张生成图片。");
    }

    private void SetGeneratedImagesStatus(string message)
    {
        GeneratedImagesStatusTextBlock.Text = message;
    }
}
