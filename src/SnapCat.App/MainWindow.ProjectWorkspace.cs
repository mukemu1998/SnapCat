using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SnapCat.App.Services;
using SnapCat.App.ViewModels;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace SnapCat.App;

public partial class MainWindow
{
    private ProjectWorkspace? _projectWorkspace;

    private async Task RestoreLastProjectWorkspaceAsync()
    {
        try
        {
            var directory = await _app.ProjectWorkspaceService.GetLastOpenedProjectDirectoryAsync();
            if (string.IsNullOrWhiteSpace(directory))
            {
                RefreshProjectWorkspaceView();
                return;
            }

            _projectWorkspace = await _app.ProjectWorkspaceService.OpenAsync(directory);
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已恢复项目：{_projectWorkspace.Project.Name}");
        }
        catch
        {
            _projectWorkspace = null;
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus("上次项目无法打开。可新建项目或重新选择项目目录。");
        }
    }

    private async void CreateProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "选择新项目的保存位置",
            UseDescriptionForTitle = true,
            InitialDirectory = _app.ProjectWorkspaceService.DefaultProjectsDirectory
        };
        if (dialog.ShowDialog() != FormsDialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            var name = ProjectNameTextBox.Text;
            _projectWorkspace = await _app.ProjectWorkspaceService.CreateAsync(dialog.SelectedPath, name);
            ProjectNameTextBox.Text = _projectWorkspace.Project.Name;
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已创建项目：{_projectWorkspace.Project.Name}");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"创建项目失败：{exception.Message}");
        }
    }

    private async void OpenProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "选择包含 project.json 的 SnapCat 项目目录",
            UseDescriptionForTitle = true,
            InitialDirectory = _projectWorkspace?.DirectoryPath ?? _app.ProjectWorkspaceService.DefaultProjectsDirectory
        };
        if (dialog.ShowDialog() != FormsDialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            _projectWorkspace = await _app.ProjectWorkspaceService.OpenAsync(dialog.SelectedPath);
            ProjectNameTextBox.Text = _projectWorkspace.Project.Name;
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已打开项目：{_projectWorkspace.Project.Name}");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"打开项目失败：{exception.Message}");
        }
    }

    private async void SaveProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        _projectWorkspace.Project.Name = ProjectNameTextBox.Text;
        try
        {
            await _app.ProjectWorkspaceService.SaveAsync(_projectWorkspace);
            ProjectNameTextBox.Text = _projectWorkspace.Project.Name;
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus("项目元数据已保存。素材与项目文件均保存在当前项目目录。" );
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"保存项目失败：{exception.Message}");
        }
    }

    private void OpenProjectDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = _projectWorkspace?.DirectoryPath ?? _app.ProjectWorkspaceService.DefaultProjectsDirectory;
        WindowsExplorerService.OpenDirectory(directory, createIfMissing: true);
        SetProjectWorkspaceStatus("已打开项目目录。" );
    }

    private async void BackupProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "选择项目备份 ZIP 的保存位置",
            UseDescriptionForTitle = true,
            InitialDirectory = _projectWorkspace.DirectoryPath
        };
        if (dialog.ShowDialog() != FormsDialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            BackupProjectButton.IsEnabled = false;
            var backupPath = await _app.ProjectWorkspaceService.CreateBackupAsync(_projectWorkspace, dialog.SelectedPath);
            SetProjectWorkspaceStatus($"项目备份已创建：{backupPath}");
            WindowsExplorerService.OpenFileOrContainingDirectory(backupPath);
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"创建项目备份失败：{exception.Message}");
        }
        finally
        {
            BackupProjectButton.IsEnabled = _projectWorkspace is not null;
        }
    }

    private async void ImportProjectAssetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            SetProjectWorkspaceStatus("请先新建或打开一个项目。" );
            return;
        }

        var dialog = new WpfOpenFileDialog
        {
            Title = "导入项目素材",
            Multiselect = true,
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var category = GetSelectedProjectAssetCategory();
        var importedCount = 0;
        var errors = new List<string>();
        foreach (var sourcePath in dialog.FileNames)
        {
            try
            {
                await _app.ProjectWorkspaceService.ImportImageAsync(
                    _projectWorkspace,
                    sourcePath,
                    ProjectAssetKind.Imported,
                    category);
                importedCount++;
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(sourcePath)}：{exception.Message}");
            }
        }

        RefreshProjectWorkspaceView();
        SetProjectWorkspaceStatus(errors.Count == 0
            ? $"已导入 {importedCount} 个素材。"
            : $"已导入 {importedCount} 个素材，{errors.Count} 个失败：{errors[0]}");
    }

    private void RefreshProjectAssetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshProjectWorkspaceView();
        SetProjectWorkspaceStatus("项目素材列表已刷新。" );
    }

    private void ProjectAssetsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = ProjectAssetsListBox.SelectedItems.Count > 0;
        DeleteSelectedProjectAssetsButton.IsEnabled = hasSelection;
        CreateProjectCollectionButton.IsEnabled = _projectWorkspace is not null && hasSelection;
        UpdateProjectCollectionButton.IsEnabled = _projectWorkspace is not null
            && hasSelection
            && ProjectCollectionsComboBox.SelectedItem is WpfComboBoxItem;
    }

    private void ProjectCollectionsComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateProjectCollectionButton.IsEnabled = _projectWorkspace is not null
            && ProjectAssetsListBox.SelectedItems.Count > 0
            && ProjectCollectionsComboBox.SelectedItem is WpfComboBoxItem;
    }

    private void ProjectRecycleAssetsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RestoreSelectedProjectAssetsButton.IsEnabled = _projectWorkspace is not null
            && ProjectRecycleAssetsListBox.SelectedItems.Count > 0;
    }

    private async void RefreshProjectRecycleBinButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshProjectRecycleBinAsync();
        SetProjectWorkspaceStatus("项目回收站已刷新。");
    }

    private async void RestoreSelectedProjectAssetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        var assetIds = ProjectRecycleAssetsListBox.SelectedItems
            .OfType<ProjectRecycleAssetListItem>()
            .Select(static item => item.Asset.Id)
            .ToList();
        if (assetIds.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择需要恢复的项目素材。");
            return;
        }

        try
        {
            RestoreSelectedProjectAssetsButton.IsEnabled = false;
            var restoredCount = await _app.ProjectWorkspaceService.RestoreFromRecycleBinAsync(_projectWorkspace, assetIds);
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus(restoredCount == 0
                ? "没有可恢复的项目素材；原文件可能已被手动移除。"
                : $"已从项目回收站恢复 {restoredCount} 个素材。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"恢复项目素材失败：{exception.Message}");
        }
    }

    private void ProjectAssetsListBox_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindParent<ListBoxItem>(source) is not { } item)
        {
            return;
        }

        if (!item.IsSelected)
        {
            ProjectAssetsListBox.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void OpenSelectedProjectAssetMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (ProjectAssetsListBox.SelectedItem is not ProjectAssetListItem item)
        {
            SetProjectWorkspaceStatus("请先选择要查看的项目素材。" );
            return;
        }

        WindowsExplorerService.OpenFileOrContainingDirectory(item.SourcePath);
    }

    private void DeleteSelectedProjectAssetsButton_OnClick(object sender, RoutedEventArgs e) =>
        DeleteSelectedProjectAssets();

    private void DeleteSelectedProjectAssetsMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        DeleteSelectedProjectAssets();

    private async void DeleteSelectedProjectAssets()
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        var selectedItems = ProjectAssetsListBox.SelectedItems.OfType<ProjectAssetListItem>().ToList();
        if (selectedItems.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择要移入回收站的素材。" );
            return;
        }

        var selectedAssetIds = selectedItems
            .Select(static item => item.Asset.Id)
            .ToHashSet(StringComparer.Ordinal);
        var affectedDerivedCount = _projectWorkspace.Project.Assets.Count(asset =>
            !string.IsNullOrWhiteSpace(asset.ParentAssetId)
            && selectedAssetIds.Contains(asset.ParentAssetId)
            && !selectedAssetIds.Contains(asset.Id));
        var confirmationMessage = $"确定将选中的 {selectedItems.Count} 个素材移入当前项目的 recycle-bin 吗？不会直接永久删除。";
        if (affectedDerivedCount > 0)
        {
            confirmationMessage += $"\n\n其中 {affectedDerivedCount} 个派生版本会暂时失去原始版本引用；恢复原素材后即可恢复版本链。";
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "移入项目回收站",
                confirmationMessage,
                "移入回收站"))
        {
            return;
        }

        try
        {
            var count = await _app.ProjectWorkspaceService.MoveToRecycleBinAsync(
                _projectWorkspace,
                selectedItems.Select(static item => item.Asset.Id));
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已将 {count} 个素材移入项目回收站。" );
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"移动素材失败：{exception.Message}");
        }
    }

    private async void ImportSelectedGeneratedImagesToProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var paths = GeneratedImagesListBox.SelectedItems
            .OfType<GeneratedImageListItem>()
            .Select(static item => item.Path)
            .ToList();
        var importedCount = await ImportExistingFilesIntoCurrentProjectAsync(paths, ProjectAssetKind.Generated);
        if (importedCount > 0)
        {
            SetGeneratedImagesStatus($"已将 {importedCount} 张生成图片保存到当前项目。" );
        }
    }

    private async void ImportSelectedDefaultCapturesToProjectButton_OnClick(object sender, RoutedEventArgs e)
    {
        var paths = DefaultCapturesListBox.SelectedItems
            .OfType<DefaultCaptureListItem>()
            .Select(static item => item.Path)
            .ToList();
        var importedCount = await ImportExistingFilesIntoCurrentProjectAsync(paths, ProjectAssetKind.Screenshot);
        if (importedCount > 0)
        {
            StatusTextBlock.Text = $"已将 {importedCount} 张截图保存到当前项目。";
        }
    }

    private async Task<int> ImportExistingFilesIntoCurrentProjectAsync(
        IReadOnlyCollection<string> sourcePaths,
        ProjectAssetKind kind)
    {
        if (_projectWorkspace is null)
        {
            SetProjectWorkspaceStatus("请先在“项目与素材库”中新建或打开一个项目。" );
            return 0;
        }

        var importedCount = 0;
        var errors = new List<string>();
        foreach (var sourcePath in sourcePaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                await _app.ProjectWorkspaceService.ImportImageAsync(
                    _projectWorkspace,
                    sourcePath,
                    kind,
                    ProjectAssetCategory.Unclassified);
                importedCount++;
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(sourcePath)}：{exception.Message}");
            }
        }

        RefreshProjectWorkspaceView();
        if (importedCount == 0)
        {
            SetProjectWorkspaceStatus(sourcePaths.Count == 0
                ? "请先选择需要保存到项目的图片。"
                : $"保存到项目失败：{errors.FirstOrDefault() ?? "没有可用图片。"}");
        }
        else if (errors.Count > 0)
        {
            SetProjectWorkspaceStatus($"已保存 {importedCount} 个素材，{errors.Count} 个失败：{errors[0]}");
        }

        return importedCount;
    }

    private async void CreateProjectCollectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        var assetIds = ProjectAssetsListBox.SelectedItems
            .OfType<ProjectAssetListItem>()
            .Select(static item => item.Asset.Id)
            .ToList();
        if (assetIds.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择需要加入集合的项目素材。");
            return;
        }

        try
        {
            var collection = await _app.ProjectWorkspaceService.CreateCollectionAsync(
                _projectWorkspace,
                ProjectCollectionNameTextBox.Text,
                assetIds);
            ProjectCollectionNameTextBox.Text = "新素材集合";
            RefreshProjectWorkspaceView(selectedCollectionId: collection.Id);
            SetProjectWorkspaceStatus($"已创建素材集合：{collection.Name}，包含 {collection.AssetIds.Count} 个素材。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"创建素材集合失败：{exception.Message}");
        }
    }

    private async void UpdateProjectCollectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null || ProjectCollectionsComboBox.SelectedItem is not WpfComboBoxItem { Tag: string collectionId })
        {
            SetProjectWorkspaceStatus("请先选择需要更新的素材集合。");
            return;
        }

        var assetIds = ProjectAssetsListBox.SelectedItems
            .OfType<ProjectAssetListItem>()
            .Select(static item => item.Asset.Id)
            .ToList();
        if (assetIds.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择要作为集合成员的项目素材。");
            return;
        }

        try
        {
            await _app.ProjectWorkspaceService.UpdateCollectionAssetsAsync(_projectWorkspace, collectionId, assetIds);
            RefreshProjectWorkspaceView(selectedCollectionId: collectionId);
            SetProjectWorkspaceStatus($"素材集合已更新，当前包含 {assetIds.Count} 个素材。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"更新素材集合失败：{exception.Message}");
        }
    }

    private void RefreshProjectWorkspaceView(string? selectedCollectionId = null)
    {
        var workspace = _projectWorkspace;
        var hasProject = workspace is not null;
        ProjectWorkspaceNameTextBlock.Text = hasProject ? workspace!.Project.Name : "未打开项目";
        ProjectWorkspacePathTextBlock.Text = hasProject
            ? workspace!.DirectoryPath
            : $"默认项目目录：{_app.ProjectWorkspaceService.DefaultProjectsDirectory}";
        OpenProjectDirectoryButton.IsEnabled = true;
        SaveProjectButton.IsEnabled = hasProject;
        BackupProjectButton.IsEnabled = hasProject;
        ImportProjectAssetsButton.IsEnabled = hasProject;
        RefreshProjectAssetsButton.IsEnabled = hasProject;
        DeleteSelectedProjectAssetsButton.IsEnabled = false;
        CreateProjectCollectionButton.IsEnabled = false;
        UpdateProjectCollectionButton.IsEnabled = false;
        RefreshProjectCollections(selectedCollectionId);
        RefreshProjectRecycleBinButton.IsEnabled = hasProject;
        RestoreSelectedProjectAssetsButton.IsEnabled = false;
        _ = RefreshProjectRecycleBinAsync();
        UpdateProjectImportActions();

        if (!hasProject)
        {
            ProjectAssetsListBox.ItemsSource = null;
            ProjectAssetsEmptyTextBlock.Visibility = Visibility.Visible;
            ProjectAssetsSummaryTextBlock.Text = "新建或打开项目后，可导入图片作为后续视觉分析、参考图与生成工作流的素材。";
            return;
        }

        var items = workspace!.Project.Assets
            .OrderByDescending(static asset => asset.CreatedAt)
            .Select(asset => new ProjectAssetListItem(
                asset,
                ResolveProjectAssetPath(workspace.DirectoryPath, asset.RelativePath),
                ResolveProjectAssetPath(workspace.DirectoryPath, asset.ThumbnailRelativePath)))
            .ToList();
        ProjectAssetsListBox.ItemsSource = items;
        ProjectAssetsEmptyTextBlock.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProjectAssetsSummaryTextBlock.Text = items.Count == 0
            ? "当前项目还没有素材。导入的图片会复制到项目目录，不依赖原文件位置。"
            : $"当前项目共 {items.Count} 个素材。图片、缩略图与 project.json 可随项目目录整体移动或备份。";
        UpdateProjectImportActions();
    }

    private async Task RefreshProjectRecycleBinAsync()
    {
        var workspace = _projectWorkspace;
        if (workspace is null)
        {
            ProjectRecycleAssetsListBox.ItemsSource = null;
            ProjectRecycleBinSummaryTextBlock.Text = "项目回收站：请先打开项目";
            return;
        }

        try
        {
            var assets = await _app.ProjectWorkspaceService.GetRecycledAssetsAsync(workspace);
            if (!ReferenceEquals(workspace, _projectWorkspace))
            {
                return;
            }

            var items = assets.Select(static asset => new ProjectRecycleAssetListItem(asset)).ToList();
            ProjectRecycleAssetsListBox.ItemsSource = items;
            ProjectRecycleBinSummaryTextBlock.Text = items.Count == 0
                ? "项目回收站：当前为空"
                : $"项目回收站：{items.Count} 个可恢复素材";
            RestoreSelectedProjectAssetsButton.IsEnabled = false;
        }
        catch
        {
            if (ReferenceEquals(workspace, _projectWorkspace))
            {
                ProjectRecycleBinSummaryTextBlock.Text = "项目回收站：读取失败";
            }
        }
    }

    private void RefreshProjectCollections(string? selectedCollectionId)
    {
        var previousId = selectedCollectionId
            ?? (ProjectCollectionsComboBox.SelectedItem as WpfComboBoxItem)?.Tag as string;
        ProjectCollectionsComboBox.Items.Clear();

        if (_projectWorkspace is null || _projectWorkspace.Project.Collections.Count == 0)
        {
            ProjectCollectionsComboBox.IsEnabled = false;
            return;
        }

        foreach (var collection in _projectWorkspace.Project.Collections.OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            ProjectCollectionsComboBox.Items.Add(new WpfComboBoxItem
            {
                Content = $"{collection.Name}（{collection.AssetIds.Count}）",
                Tag = collection.Id
            });
        }

        ProjectCollectionsComboBox.IsEnabled = true;
        var selectedItem = ProjectCollectionsComboBox.Items
            .OfType<WpfComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, previousId, StringComparison.Ordinal));
        ProjectCollectionsComboBox.SelectedItem = selectedItem ?? ProjectCollectionsComboBox.Items[0];
    }

    private void UpdateProjectImportActions()
    {
        var hasProject = _projectWorkspace is not null;
        if (ImportSelectedGeneratedImagesToProjectButton is not null)
        {
            ImportSelectedGeneratedImagesToProjectButton.IsEnabled = hasProject
                && GeneratedImagesListBox.SelectedItems.Count > 0;
        }

        if (ImportSelectedDefaultCapturesToProjectButton is not null)
        {
            ImportSelectedDefaultCapturesToProjectButton.IsEnabled = hasProject
                && DefaultCapturesListBox.SelectedItems.Count > 0;
        }
    }

    private static string ResolveProjectAssetPath(string projectDirectory, string relativePath) =>
        string.IsNullOrWhiteSpace(relativePath) ? string.Empty : Path.Combine(projectDirectory, relativePath);

    private ProjectAssetCategory GetSelectedProjectAssetCategory()
    {
        return ProjectAssetCategoryComboBox.SelectedItem is WpfComboBoxItem { Tag: string tag }
               && Enum.TryParse<ProjectAssetCategory>(tag, out var category)
            ? category
            : ProjectAssetCategory.Unclassified;
    }

    private void SetProjectWorkspaceStatus(string message)
    {
        ProjectAssetsStatusTextBlock.Text = message;
    }
}
