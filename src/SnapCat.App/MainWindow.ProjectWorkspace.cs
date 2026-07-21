using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SnapCat.App.Services;
using SnapCat.App.ViewModels;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using FormsFolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfClipboard = System.Windows.Clipboard;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace SnapCat.App;

public partial class MainWindow
{
    private ProjectWorkspace? _projectWorkspace;
    private string? _editingProjectDirectory;
    private string _projectAssetCategoryFilterKey = "all";
    private bool _isRefreshingProjectAssetCategoryFilters;

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
        var suggestedName = $"新项目 {DateTime.Now:MMdd HHmm}";
        if (!ProjectNameDialogWindow.TryGetName(this, suggestedName, out var name))
        {
            return;
        }

        try
        {
            _editingProjectDirectory = null;
            _projectWorkspace = await _app.ProjectWorkspaceService.CreateAsync(
                _app.ProjectWorkspaceService.DefaultProjectsDirectory,
                name);
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已创建项目：{_projectWorkspace.Project.Name}");
            OpenAiCanvas(_projectWorkspace);
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"创建项目失败：{exception.Message}");
        }
    }

    private async void ChooseDefaultProjectsDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new FormsFolderBrowserDialog
        {
            Description = "选择 SnapCat AI 画布项目的默认保存目录",
            UseDescriptionForTitle = true,
            SelectedPath = _app.ProjectWorkspaceService.DefaultProjectsDirectory,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != FormsDialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        try
        {
            await _app.ProjectWorkspaceService.SetDefaultProjectsDirectoryAsync(dialog.SelectedPath);
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus("默认项目目录已更新。后续新建项目和本地项目列表将使用此目录。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"设置默认项目目录失败：{exception.Message}");
        }
    }

    private void OpenDefaultProjectsDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowsExplorerService.OpenDirectory(_app.ProjectWorkspaceService.DefaultProjectsDirectory, createIfMissing: true);
        SetProjectWorkspaceStatus("已打开默认项目目录。");
    }

    private async void ResetDefaultProjectsDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _app.ProjectWorkspaceService.ResetDefaultProjectsDirectoryAsync();
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus("默认项目目录已恢复为 SnapCat 用户数据目录下的“项目”文件夹。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"恢复默认项目目录失败：{exception.Message}");
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
            _editingProjectDirectory = null;
            _projectWorkspace = await _app.ProjectWorkspaceService.OpenAsync(dialog.SelectedPath);
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

        try
        {
            await _app.ProjectWorkspaceService.SaveAsync(_projectWorkspace);
            _editingProjectDirectory = null;
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

    private void OpenAiCanvasButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            SetProjectWorkspaceStatus("请先新建或打开项目，再进入 AI 创作画布。" );
            return;
        }

        var selectedAssetIds = ProjectAssetsListBox.SelectedItems
            .OfType<ProjectAssetListItem>()
            .Select(static item => item.Asset.Id)
            .ToArray();
        OpenAiCanvas(_projectWorkspace, selectedAssetIds);
    }

    private void OpenAiCanvas(ProjectWorkspace workspace, IEnumerable<string>? initialAssetIds = null)
    {
        var canvasWindow = new ProjectAiCanvasWindow(
            workspace,
            _app.ProjectWorkspaceService,
            _app.AiCanvasWorkspaceService,
            initialAssetIds ?? [],
            ImageGenerationProfile.CloneAll(_settings.ImageGenerationProfiles),
            _app.ImageGenerationService);
        canvasWindow.Show();
    }

    private async void RefreshLocalProjectsButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RefreshLocalProjectsAsync();
        SetProjectWorkspaceStatus("本地项目列表已刷新。");
    }

    private async void SelectProjectCardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ProjectWorkspaceCardItem item })
        {
            return;
        }

        if (string.Equals(_projectWorkspace?.DirectoryPath, item.Summary.DirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            _editingProjectDirectory = null;
            _projectWorkspace = null;
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus("已取消项目选中。");
            return;
        }

        await OpenProjectCardAsync(item);
    }

    private async void EditProjectCardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ProjectWorkspaceCardItem item })
        {
            return;
        }

        if (await OpenProjectCardAsync(item))
        {
            _editingProjectDirectory = item.Summary.DirectoryPath;
            await RefreshLocalProjectsAsync();
            FocusProjectCardNameEditor(item.Summary.DirectoryPath);
            SetProjectWorkspaceStatus("可直接在封面卡片标题处修改项目名称，按回车或离开输入框后保存。" );
        }
    }

    private async void DeleteProjectCardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ProjectWorkspaceCardItem item })
        {
            return;
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "删除项目",
                $"确定删除项目“{item.Name}”吗？将删除该项目目录、项目素材、画布和项目回收站内容，且无法恢复。",
                "删除"))
        {
            return;
        }

        try
        {
            _editingProjectDirectory = null;
            await _app.ProjectWorkspaceService.DeleteLocalProjectAsync(item.Summary.DirectoryPath);
            if (string.Equals(_projectWorkspace?.DirectoryPath, item.Summary.DirectoryPath, StringComparison.OrdinalIgnoreCase))
            {
                _projectWorkspace = null;
            }

            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已删除项目：{item.Name}");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"删除项目失败：{exception.Message}");
        }
    }

    private async Task<bool> OpenProjectCardAsync(ProjectWorkspaceCardItem item)
    {
        try
        {
            _editingProjectDirectory = null;
            _projectWorkspace = await _app.ProjectWorkspaceService.OpenAsync(item.Summary.DirectoryPath);
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已打开项目：{_projectWorkspace.Project.Name}");
            return true;
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"打开项目失败：{exception.Message}");
            return false;
        }
    }

    private async void ProjectCardNameTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not WpfTextBox { Tag: ProjectWorkspaceCardItem item })
        {
            return;
        }

        e.Handled = true;
        await SaveProjectCardNameAsync(item);
    }

    private async void ProjectCardNameTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is WpfTextBox { Tag: ProjectWorkspaceCardItem item })
        {
            await SaveProjectCardNameAsync(item);
        }
    }

    private async Task SaveProjectCardNameAsync(ProjectWorkspaceCardItem item)
    {
        if (_projectWorkspace is null ||
            !string.Equals(_projectWorkspace.DirectoryPath, item.Summary.DirectoryPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var name = item.EditableName.Trim();
        _editingProjectDirectory = null;
        if (string.IsNullOrWhiteSpace(name))
        {
            RefreshProjectWorkspaceView();
            return;
        }

        if (string.Equals(_projectWorkspace.Project.Name, name, StringComparison.Ordinal))
        {
            RefreshProjectWorkspaceView();
            return;
        }

        try
        {
            _projectWorkspace = await _app.ProjectWorkspaceService.RenameAsync(_projectWorkspace, name);
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"项目名称已修改为：{_projectWorkspace.Project.Name}");
        }
        catch (Exception exception)
        {
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"保存项目名称失败：{exception.Message}");
        }
    }

    private void FocusProjectCardNameEditor(string directoryPath)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            var textBox = FindVisualDescendant<WpfTextBox>(LocalProjectsItemsControl, candidate =>
                candidate.Name == "ProjectCardNameTextBox" &&
                candidate.Tag is ProjectWorkspaceCardItem item &&
                string.Equals(item.Summary.DirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase));
            if (textBox is null)
            {
                return;
            }

            textBox.Focus();
            textBox.SelectAll();
        }));
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match)
            {
                return match;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject? element, Func<T, bool> predicate) where T : DependencyObject
    {
        if (element is null)
        {
            return null;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is T match && predicate(match))
            {
                return match;
            }

            var descendant = FindVisualDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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

        await ImportProjectAssetFilesAsync(dialog.FileNames, ProjectAssetKind.Imported, "导入项目素材");
    }

    private async Task ImportProjectAssetFilesAsync(
        IEnumerable<string> sourcePaths,
        ProjectAssetKind kind,
        string actionName)
    {
        if (_projectWorkspace is null)
        {
            SetProjectWorkspaceStatus("请先新建或打开一个项目。");
            return;
        }

        var files = sourcePaths
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            SetProjectWorkspaceStatus("没有找到可导入的图片文件。");
            return;
        }

        var categorySelection = ProjectAssetCategoryDialogWindow.Select(this, files.Length, "确认导入");
        if (categorySelection is null)
        {
            SetProjectWorkspaceStatus("已取消导入项目素材。");
            return;
        }

        var importedCount = 0;
        var errors = new List<string>();
        foreach (var sourcePath in files)
        {
            try
            {
                await _app.ProjectWorkspaceService.ImportImageAsync(
                    _projectWorkspace,
                    sourcePath,
                    kind,
                    categorySelection.Category,
                    customCategory: categorySelection.CustomCategory);
                importedCount++;
            }
            catch (Exception exception)
            {
                errors.Add($"{Path.GetFileName(sourcePath)}：{exception.Message}");
            }
        }

        RefreshProjectWorkspaceView();
        SetProjectWorkspaceStatus(errors.Count == 0
            ? $"{actionName}完成：已导入 {importedCount} 个素材。"
            : $"{actionName}完成：已导入 {importedCount} 个素材，{errors.Count} 个失败：{errors[0]}");
    }

    private void ProjectAssetsListBox_OnDragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = _projectWorkspace is not null && e.Data.GetDataPresent(WpfDataFormats.FileDrop)
            ? WpfDragDropEffects.Copy
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void ProjectAssetsEmptyDropZone_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_projectWorkspace is null || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        ProjectAssetsEmptyTextBlock.Focus();
        ImportProjectAssetsButton_OnClick(sender, e);
        e.Handled = true;
    }

    private async void ProjectAssetsListBox_OnDrop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData(WpfDataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        await ImportProjectAssetFilesAsync(files, ProjectAssetKind.Imported, "拖入项目素材");
        e.Handled = true;
    }

    private async void ProjectAssetsListBox_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        if (WpfClipboard.ContainsFileDropList())
        {
            await ImportProjectAssetFilesAsync(
                WpfClipboard.GetFileDropList().Cast<string>(),
                ProjectAssetKind.Imported,
                "粘贴项目素材");
            e.Handled = true;
            return;
        }

        if (!WpfClipboard.ContainsImage() || WpfClipboard.GetImage() is not BitmapSource image)
        {
            return;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"SnapCat-project-asset-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            await ImportProjectAssetFilesAsync([temporaryPath], ProjectAssetKind.Imported, "粘贴项目素材");
            e.Handled = true;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
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
    }

    private void ProjectRecycleAssetsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = _projectWorkspace is not null && ProjectRecycleAssetsListBox.SelectedItems.Count > 0;
        RestoreSelectedProjectAssetsButton.IsEnabled = hasSelection;
        DeleteSelectedRecycledProjectAssetsButton.IsEnabled = hasSelection;
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

    private async void DeleteSelectedRecycledProjectAssetsButton_OnClick(object sender, RoutedEventArgs e)
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
            SetProjectWorkspaceStatus("请先选择需要永久删除的回收站素材。");
            return;
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "永久删除回收站素材",
                $"确定永久删除选中的 {assetIds.Count} 个回收站素材吗？此操作无法恢复。",
                "永久删除"))
        {
            return;
        }

        try
        {
            DeleteSelectedRecycledProjectAssetsButton.IsEnabled = false;
            var deletedCount = await _app.ProjectWorkspaceService.DeleteFromRecycleBinAsync(_projectWorkspace, assetIds);
            await RefreshProjectRecycleBinAsync();
            SetProjectWorkspaceStatus(deletedCount == 0
                ? "没有找到可永久删除的回收站素材。"
                : $"已永久删除 {deletedCount} 个回收站素材。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"永久删除回收站素材失败：{exception.Message}");
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

    private async void ChangeSelectedProjectAssetCategoryMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null || sender is not WpfMenuItem { Tag: string tag } ||
            !Enum.TryParse<ProjectAssetCategory>(tag, out var category))
        {
            return;
        }

        var selectedItems = ProjectAssetsListBox.SelectedItems.OfType<ProjectAssetListItem>().ToList();
        if (selectedItems.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择需要调整分类的素材。");
            return;
        }

        var changedCount = await _app.ProjectWorkspaceService.UpdateAssetCategoriesAsync(
            _projectWorkspace,
            selectedItems.Select(static item => item.Asset.Id),
            category);
        RefreshProjectWorkspaceView();
        SetProjectWorkspaceStatus(changedCount == 0
            ? "选中素材已经属于该分类。"
            : $"已更新 {changedCount} 个素材的分类。");
    }

    private async void SetCustomProjectAssetCategoryMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        var selectedItems = ProjectAssetsListBox.SelectedItems.OfType<ProjectAssetListItem>().ToList();
        if (selectedItems.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择需要调整分类的素材。");
            return;
        }

        var selection = ProjectAssetCategoryDialogWindow.Select(this, selectedItems.Count, "应用分类");
        if (selection is null)
        {
            return;
        }

        var changedCount = await _app.ProjectWorkspaceService.UpdateAssetCategoriesAsync(
            _projectWorkspace,
            selectedItems.Select(static item => item.Asset.Id),
            selection.Category,
            customCategory: selection.CustomCategory);
        RefreshProjectWorkspaceView();
        SetProjectWorkspaceStatus(changedCount == 0
            ? "选中素材的分类没有变化。"
            : $"已更新 {changedCount} 个素材的分类。");
    }

    private void SelectProjectAssetCategoryMenuItem_OnClick(object sender, RoutedEventArgs e) =>
        SetCustomProjectAssetCategoryMenuItem_OnClick(sender, e);

    private async void PermanentlyDeleteSelectedProjectAssetsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (_projectWorkspace is null)
        {
            return;
        }

        var selectedItems = ProjectAssetsListBox.SelectedItems.OfType<ProjectAssetListItem>().ToList();
        if (selectedItems.Count == 0)
        {
            SetProjectWorkspaceStatus("请先选择需要彻底删除的素材。");
            return;
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "彻底删除素材",
                $"确定彻底删除选中的 {selectedItems.Count} 个素材吗？图片文件、缩略图与项目中的素材记录会被移除，且无法恢复。",
                "彻底删除"))
        {
            return;
        }

        try
        {
            var deletedCount = await _app.ProjectWorkspaceService.DeleteAssetsPermanentlyAsync(
                _projectWorkspace,
                selectedItems.Select(static item => item.Asset.Id));
            RefreshProjectWorkspaceView();
            SetProjectWorkspaceStatus($"已彻底删除 {deletedCount} 个项目素材。");
        }
        catch (Exception exception)
        {
            SetProjectWorkspaceStatus($"彻底删除素材失败：{exception.Message}");
        }
    }

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
        var confirmationMessage = $"确定将选中的 {selectedItems.Count} 个素材移入当前项目的回收站吗？不会直接永久删除。";
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

    private void ProjectAssetCategoryFilterComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingProjectAssetCategoryFilters ||
            ProjectAssetCategoryFilterComboBox.SelectedItem is not WpfComboBoxItem { Tag: string filterKey })
        {
            return;
        }

        _projectAssetCategoryFilterKey = filterKey;
        RefreshProjectWorkspaceView();
    }

    private void RefreshProjectAssetCategoryFilters(IEnumerable<ProjectAsset> assets)
    {
        _isRefreshingProjectAssetCategoryFilters = true;
        try
        {
            ProjectAssetCategoryFilterComboBox.Items.Clear();
            AddProjectAssetCategoryFilterItem("全部分类", "all");
            AddProjectAssetCategoryFilterItem("未分类", ProjectAssetCategory.Unclassified.ToString());
            AddProjectAssetCategoryFilterItem("角色", ProjectAssetCategory.Character.ToString());
            AddProjectAssetCategoryFilterItem("场景", ProjectAssetCategory.Scene.ToString());
            AddProjectAssetCategoryFilterItem("道具", ProjectAssetCategory.Prop.ToString());
            AddProjectAssetCategoryFilterItem("风格参考", ProjectAssetCategory.StyleReference.ToString());

            foreach (var customCategory in assets
                         .Select(static asset => asset.CustomCategory)
                         .Where(static value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase))
            {
                AddProjectAssetCategoryFilterItem(customCategory, $"custom:{customCategory}");
            }

            var validKeys = ProjectAssetCategoryFilterComboBox.Items
                .OfType<WpfComboBoxItem>()
                .Select(static item => item.Tag as string)
                .ToHashSet(StringComparer.Ordinal);
            if (!validKeys.Contains(_projectAssetCategoryFilterKey))
            {
                _projectAssetCategoryFilterKey = "all";
            }

            ProjectAssetCategoryFilterComboBox.SelectedItem = ProjectAssetCategoryFilterComboBox.Items
                .OfType<WpfComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, _projectAssetCategoryFilterKey, StringComparison.Ordinal));
        }
        finally
        {
            _isRefreshingProjectAssetCategoryFilters = false;
        }
    }

    private void AddProjectAssetCategoryFilterItem(string label, string filterKey)
    {
        ProjectAssetCategoryFilterComboBox.Items.Add(new WpfComboBoxItem
        {
            Content = label,
            Tag = filterKey
        });
    }

    private bool MatchesProjectAssetCategoryFilter(ProjectAsset asset) =>
        _projectAssetCategoryFilterKey switch
        {
            "all" => true,
            var filter when filter.StartsWith("custom:", StringComparison.Ordinal) =>
                string.Equals(asset.CustomCategory, filter["custom:".Length..], StringComparison.OrdinalIgnoreCase),
            var filter when Enum.TryParse<ProjectAssetCategory>(filter, out var category) =>
                string.IsNullOrWhiteSpace(asset.CustomCategory) && asset.Category == category,
            _ => true
        };

    private void RefreshProjectWorkspaceView()
    {
        _ = RefreshLocalProjectsAsync();
        DefaultProjectsDirectoryTextBox.Text = _app.ProjectWorkspaceService.DefaultProjectsDirectory;
        var workspace = _projectWorkspace;
        var hasProject = workspace is not null;
        OpenAiCanvasButton.IsEnabled = hasProject;
        ProjectAssetsPanelBorder.Visibility = hasProject ? Visibility.Visible : Visibility.Collapsed;
        ImportProjectAssetsButton.IsEnabled = hasProject;
        RefreshProjectAssetsButton.IsEnabled = hasProject;
        DeleteSelectedProjectAssetsButton.IsEnabled = false;
        RefreshProjectRecycleBinButton.IsEnabled = hasProject;
        RestoreSelectedProjectAssetsButton.IsEnabled = false;
        DeleteSelectedRecycledProjectAssetsButton.IsEnabled = false;
        _ = RefreshProjectRecycleBinAsync();
        UpdateProjectImportActions();

        if (!hasProject)
        {
            ProjectAssetCategoryFilterComboBox.Items.Clear();
            ProjectAssetsListBox.ItemsSource = null;
            ProjectAssetsEmptyTextBlock.Visibility = Visibility.Visible;
            ProjectAssetsSummaryTextBlock.Text = "请选择一个本地项目后查看对应素材。";
            return;
        }

        var allAssets = workspace!.Project.Assets
            .OrderByDescending(static asset => asset.CreatedAt)
            .ToList();
        RefreshProjectAssetCategoryFilters(allAssets);
        var items = allAssets
            .Where(MatchesProjectAssetCategoryFilter)
            .Select(asset => new ProjectAssetListItem(
                asset,
                ResolveProjectAssetPath(workspace.DirectoryPath, asset.RelativePath),
                ResolveProjectAssetPath(workspace.DirectoryPath, asset.ThumbnailRelativePath)))
            .ToList();
        ProjectAssetsListBox.ItemsSource = items;
        ProjectAssetsEmptyTextBlock.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProjectAssetsEmptyTitleTextBlock.Text = allAssets.Count == 0 ? "当前没有项目素材" : "当前分类没有素材";
        ProjectAssetsEmptyHintTextBlock.Text = allAssets.Count == 0
            ? "点击导入图片，或直接拖入、按 Ctrl+V 粘贴图片"
            : "可切换分类，或直接拖入、按 Ctrl+V 粘贴图片到当前分类";
        ProjectAssetsSummaryTextBlock.Text = allAssets.Count == 0
            ? "当前项目还没有素材。导入的图片会复制到项目目录，不依赖原文件位置。"
            : $"当前显示 {items.Count} / {allAssets.Count} 个素材。图片、缩略图与 project.json 可随项目目录整体移动或备份。";
        UpdateProjectImportActions();
    }

    private async Task RefreshLocalProjectsAsync()
    {
        try
        {
            var summaries = await _app.ProjectWorkspaceService.ListLocalProjectsAsync();
            var selectedDirectoryPath = _projectWorkspace?.DirectoryPath;
            var editingDirectoryPath = _editingProjectDirectory;
            var cards = summaries.Select(summary => new ProjectWorkspaceCardItem(
                summary,
                isSelected: string.Equals(summary.DirectoryPath, selectedDirectoryPath, StringComparison.OrdinalIgnoreCase),
                isEditing: string.Equals(summary.DirectoryPath, editingDirectoryPath, StringComparison.OrdinalIgnoreCase))).ToList();
            var currentWorkspace = _projectWorkspace;
            if (currentWorkspace is not null && !cards.Any(card =>
                    string.Equals(card.Summary.DirectoryPath, currentWorkspace.DirectoryPath, StringComparison.OrdinalIgnoreCase)))
            {
                cards.Insert(0, CreateCurrentProjectCard(
                    currentWorkspace,
                    isSelected: true,
                    isEditing: string.Equals(currentWorkspace.DirectoryPath, editingDirectoryPath, StringComparison.OrdinalIgnoreCase)));
            }

            LocalProjectsItemsControl.ItemsSource = cards;
            LocalProjectsEmptyTextBlock.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            LocalProjectsItemsControl.ItemsSource = null;
            LocalProjectsEmptyTextBlock.Visibility = Visibility.Visible;
            LocalProjectsEmptyTextBlock.Text = "本地项目列表读取失败。可使用“打开其他项目”选择已有项目。";
        }
    }

    private static ProjectWorkspaceCardItem CreateCurrentProjectCard(ProjectWorkspace workspace, bool isSelected, bool isEditing)
    {
        var coverAsset = workspace.Project.Assets
            .OrderBy(static asset => asset.CreatedAt)
            .FirstOrDefault(asset => !string.IsNullOrWhiteSpace(asset.ThumbnailRelativePath) || !string.IsNullOrWhiteSpace(asset.RelativePath));
        var coverPath = coverAsset?.ThumbnailRelativePath;
        if (string.IsNullOrWhiteSpace(coverPath))
        {
            coverPath = coverAsset?.RelativePath ?? string.Empty;
        }

        return new ProjectWorkspaceCardItem(new ProjectWorkspaceSummary
        {
            DirectoryPath = workspace.DirectoryPath,
            ProjectId = workspace.Project.Id,
            Name = workspace.Project.Name,
            UpdatedAt = workspace.Project.UpdatedAt,
            AssetCount = workspace.Project.Assets.Count,
            CoverImageRelativePath = coverPath
        }, isSelected: isSelected, isEditing: isEditing);
    }

    private async Task RefreshProjectRecycleBinAsync()
    {
        var workspace = _projectWorkspace;
        if (workspace is null)
        {
            ProjectRecycleAssetsListBox.ItemsSource = null;
            ProjectRecycleBinSummaryTextBlock.Text = "项目回收站：请先打开项目";
            DeleteSelectedRecycledProjectAssetsButton.IsEnabled = false;
            return;
        }

        try
        {
            var assets = await _app.ProjectWorkspaceService.GetRecycledAssetsAsync(workspace);
            if (!ReferenceEquals(workspace, _projectWorkspace))
            {
                return;
            }

            var recycleDirectory = ResolveProjectRecycleDirectory(workspace.DirectoryPath);
            var items = assets.Select(asset => new ProjectRecycleAssetListItem(asset, recycleDirectory)).ToList();
            ProjectRecycleAssetsListBox.ItemsSource = items;
            ProjectRecycleBinSummaryTextBlock.Text = items.Count == 0
                ? "项目回收站：当前为空"
                : $"项目回收站：{items.Count} 个可恢复素材";
            RestoreSelectedProjectAssetsButton.IsEnabled = false;
            DeleteSelectedRecycledProjectAssetsButton.IsEnabled = false;
        }
        catch
        {
            if (ReferenceEquals(workspace, _projectWorkspace))
            {
                ProjectRecycleBinSummaryTextBlock.Text = "项目回收站：读取失败";
            }
        }
    }

    private static string ResolveProjectRecycleDirectory(string projectDirectory)
    {
        var legacyDirectory = Path.Combine(projectDirectory, "recycle-bin");
        return Directory.Exists(legacyDirectory)
            ? legacyDirectory
            : Path.Combine(projectDirectory, "回收站");
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

    private void SetProjectWorkspaceStatus(string message)
    {
        ProjectAssetsStatusTextBlock.Text = message;
    }
}
