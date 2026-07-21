using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SnapCat.App.ViewModels;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfCursors = System.Windows.Input.Cursors;
using WpfImage = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfLine = System.Windows.Shapes.Line;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;

namespace SnapCat.App.Windows;

public partial class ProjectAiCanvasWindow : Window
{
    private readonly ProjectWorkspace _workspace;
    private readonly IProjectWorkspaceService _projectWorkspaceService;
    private readonly IAiCanvasWorkspaceService _canvasService;
    private readonly IReadOnlyList<string> _initialAssetIds;
    private readonly IImageGenerationService _imageGenerationService;
    private readonly IReadOnlyList<ImageGenerationProfile> _generationProfiles;
    private readonly Dictionary<string, Border> _nodeCards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _selectedNodeIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WpfPoint> _dragStartPositions = new(StringComparer.Ordinal);
    private readonly WpfRectangle _selectionRectangle = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Canvas _centerGridReference = new() { IsHitTestVisible = false };
    private AiCanvasDocument? _document;
    private AiCanvasAssetNode? _draggedNode;
    private WpfPoint _dragStart;
    private WpfPoint _nodeStart;
    private bool _isPanning;
    private bool _isInspectorVisible = true;
    private bool _isInspectorAutoCollapsed;
    private WpfPoint _panStart;
    private WpfPoint _viewportStart;
    private WpfPoint _selectionStartWorld;
    private bool _isSelectingNodes;
    private WpfPoint _referenceDragStart;
    private ProjectAssetListItem? _referenceDragItem;
    private CancellationTokenSource? _generationCancellation;

    private const string ReferenceAssetIdsDragFormat = "SnapCat.AiCanvas.ReferenceAssetIds";

    public ProjectAiCanvasWindow(
        ProjectWorkspace workspace,
        IProjectWorkspaceService projectWorkspaceService,
        IAiCanvasWorkspaceService canvasService,
        IEnumerable<string> initialAssetIds,
        IEnumerable<ImageGenerationProfile> generationProfiles,
        IImageGenerationService imageGenerationService)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _projectWorkspaceService = projectWorkspaceService ?? throw new ArgumentNullException(nameof(projectWorkspaceService));
        _canvasService = canvasService ?? throw new ArgumentNullException(nameof(canvasService));
        _initialAssetIds = initialAssetIds?.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray()
            ?? [];
        _generationProfiles = ImageGenerationProfile.CloneAll(generationProfiles)
            .Where(static profile => profile.IsEnabled)
            .ToArray();
        _imageGenerationService = imageGenerationService ?? throw new ArgumentNullException(nameof(imageGenerationService));
        InitializeComponent();
        ProjectNameTextBlock.Text = $"项目：{_workspace.Project.Name}";
        InitializeCanvasGuides();
        InitializeGenerationControls();
        Loaded += async (_, _) => await LoadCanvasAsync();
    }

    private async Task LoadCanvasAsync()
    {
        try
        {
            _document = await _canvasService.LoadAsync(_workspace);
            if (_initialAssetIds.Count > 0)
            {
                _document = await _canvasService.AddAssetNodesAsync(_workspace, _document, _initialAssetIds);
            }

            ApplyViewport();
            RenderNodes();
            RefreshReferenceAssets();
            ApplyGenerationDraft();
            UpdateGenerationActionState();
            CanvasStatusTextBlock.Text = _initialAssetIds.Count > 0
                ? $"已将 {_initialAssetIds.Count} 个当前选中素材加入画布。"
                : "画布已恢复。可返回项目素材库选中图片后再次打开画布。";
        }
        catch (Exception exception)
        {
            CanvasStatusTextBlock.Text = $"加载画布失败：{exception.Message}";
        }
    }

    private void InitializeGenerationControls()
    {
        GenerationProfileComboBox.ItemsSource = _generationProfiles;
        GenerationProfileComboBox.SelectedItem = _generationProfiles.FirstOrDefault(static profile => profile.IsDefault)
            ?? _generationProfiles.FirstOrDefault();
        CanvasAspectRatioComboBox.SelectedIndex = 0;
        CanvasReferenceIntentComboBox.SelectedIndex = 0;
        CanvasOutputCountComboBox.SelectedIndex = 0;
        UpdateCanvasPromptHint();
    }

    private void ToggleInspectorButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isInspectorAutoCollapsed = false;
        SetInspectorVisible(!_isInspectorVisible);
    }

    private void ExpandInspectorButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isInspectorAutoCollapsed = false;
        SetInspectorVisible(true);
    }

    private void SetInspectorVisible(bool isVisible)
    {
        _isInspectorVisible = isVisible;
        InspectorBorder.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        InspectorColumn.Width = isVisible ? new GridLength(308d) : new GridLength(0d);
        ExpandInspectorButton.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        ToggleInspectorButton.Content = "收起";
    }

    private void ToggleAdvancedGenerationButton_OnClick(object sender, RoutedEventArgs e)
    {
        var isExpanded = AdvancedGenerationOptionsBorder.Visibility == Visibility.Visible;
        AdvancedGenerationOptionsBorder.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        if (!isExpanded)
        {
            CanvasNegativePromptTextBox.Focus();
        }
    }

    private void ApplyGenerationDraft()
    {
        if (_document is null)
        {
            return;
        }

        var draft = _document.GenerationDraft;
        CanvasPromptTextBox.Text = draft.Prompt;
        CanvasNegativePromptTextBox.Text = draft.NegativePrompt;
        SelectComboBoxItemByTag(CanvasAspectRatioComboBox, draft.AspectRatio);
        SelectComboBoxItemByTag(CanvasReferenceIntentComboBox, draft.ReferenceIntent);
        SelectComboBoxItemByTag(CanvasOutputCountComboBox, draft.OutputCount.ToString());
        UpdateCanvasPromptHint();
    }

    private void CanvasPromptTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => UpdateCanvasPromptHint();

    private void CanvasNegativePromptTextBox_OnTextChanged(object sender, TextChangedEventArgs e) => UpdateCanvasPromptHint();

    private void UpdateCanvasPromptHint()
    {
        CanvasPromptHintTextBlock.Visibility = string.IsNullOrWhiteSpace(CanvasPromptTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        CanvasNegativePromptHintTextBlock.Visibility = string.IsNullOrWhiteSpace(CanvasNegativePromptTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SaveGenerationDraftFromControls()
    {
        if (_document is null)
        {
            return;
        }

        _document.GenerationDraft.Prompt = CanvasPromptTextBox.Text ?? string.Empty;
        _document.GenerationDraft.NegativePrompt = CanvasNegativePromptTextBox.Text ?? string.Empty;
        _document.GenerationDraft.AspectRatio = GetSelectedTag(CanvasAspectRatioComboBox, "1:1");
        _document.GenerationDraft.ReferenceIntent = GetSelectedTag(CanvasReferenceIntentComboBox, "综合参考");
        _document.GenerationDraft.OutputCount = int.TryParse(GetSelectedTag(CanvasOutputCountComboBox, "1"), out var outputCount)
            ? Math.Clamp(outputCount, 1, 8)
            : 1;
    }

    private async void GenerateOnCanvasButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        SaveGenerationDraftFromControls();
        var profile = GenerationProfileComboBox.SelectedItem as ImageGenerationProfile;
        if (profile is null)
        {
            CanvasStatusTextBlock.Text = "请先在图像生成设置中添加并启用 ComfyUI 配置。";
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultCheckpoint))
        {
            CanvasStatusTextBlock.Text = "当前配置尚未选择 Checkpoint，请先在图像生成设置中完成配置。";
            return;
        }

        var prompt = _document.GenerationDraft.Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            CanvasStatusTextBlock.Text = "请输入用于生图的提示词。";
            CanvasPromptTextBox.Focus();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _generationCancellation = cancellation;
        UpdateGenerationActionState();
        CanvasStatusTextBlock.Text = $"正在使用“{profile.Name}”生成 {_document.GenerationDraft.OutputCount} 张结果…";

        try
        {
            var (width, height) = BuildGenerationDimensions(profile, _document.GenerationDraft.AspectRatio);
            var result = await _imageGenerationService.GenerateAsync(
                new ImageGenerationRequest
                {
                    Prompt = prompt,
                    NegativePrompt = _document.GenerationDraft.NegativePrompt,
                    Checkpoint = profile.DefaultCheckpoint,
                    Width = width,
                    Height = height,
                    Steps = profile.DefaultSteps,
                    CfgScale = profile.DefaultCfgScale,
                    OutputCount = _document.GenerationDraft.OutputCount
                },
                profile,
                cancellation.Token);

            if (!result.Success || result.Outputs.Count == 0)
            {
                CanvasStatusTextBlock.Text = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? "ComfyUI 没有返回可导入的图片。"
                    : result.ErrorMessage;
                return;
            }

            var generatedAssetIds = await ImportGeneratedOutputsAsync(result.Outputs, cancellation.Token);
            if (generatedAssetIds.Count == 0)
            {
                CanvasStatusTextBlock.Text = "生成完成，但结果导入项目失败。";
                return;
            }

            _document = await _canvasService.AddAssetNodesAsync(_workspace, _document, generatedAssetIds, cancellation.Token);
            await _canvasService.SaveAsync(_workspace, _document, cancellation.Token);
            RenderNodes();
            RefreshReferenceAssets(generatedAssetIds);
            CanvasStatusTextBlock.Text = $"已生成并导入 {generatedAssetIds.Count} 张图片到当前项目和画布。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            CanvasStatusTextBlock.Text = "已停止等待 ComfyUI 结果；本地队列中的任务可能仍会继续执行。";
        }
        catch (Exception exception)
        {
            CanvasStatusTextBlock.Text = $"生成或导入失败：{exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_generationCancellation, cancellation))
            {
                _generationCancellation = null;
            }

            cancellation.Dispose();
            UpdateGenerationActionState();
        }
    }

    private void CancelCanvasGenerationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_generationCancellation is not { IsCancellationRequested: false })
        {
            return;
        }

        _generationCancellation.Cancel();
        CancelCanvasGenerationButton.IsEnabled = false;
        CanvasStatusTextBlock.Text = "正在停止等待 ComfyUI 结果…";
    }

    private async Task<IReadOnlyList<string>> ImportGeneratedOutputsAsync(
        IEnumerable<ImageGenerationOutput> outputs,
        CancellationToken cancellationToken)
    {
        var generatedAssetIds = new List<string>();
        foreach (var output in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Content.Length == 0)
            {
                continue;
            }

            var extension = Path.GetExtension(output.FileName);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".png";
            }

            var temporaryPath = Path.Combine(Path.GetTempPath(), $"SnapCat-canvas-result-{Guid.NewGuid():N}{extension}");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, output.Content, cancellationToken);
                var asset = await _projectWorkspaceService.ImportImageAsync(
                    _workspace,
                    temporaryPath,
                    ProjectAssetKind.Generated,
                    ProjectAssetCategory.Unclassified,
                    cancellationToken);
                asset.DisplayName = string.IsNullOrWhiteSpace(output.FileName)
                    ? $"画布生成结果 {generatedAssetIds.Count + 1}"
                    : output.FileName;
                generatedAssetIds.Add(asset.Id);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        if (generatedAssetIds.Count > 0)
        {
            await _projectWorkspaceService.SaveAsync(_workspace, cancellationToken);
        }

        return generatedAssetIds;
    }

    private void UpdateGenerationActionState()
    {
        var isGenerating = _generationCancellation is not null;
        GenerateOnCanvasButton.IsEnabled = !isGenerating && _generationProfiles.Count > 0;
        CancelCanvasGenerationButton.IsEnabled = isGenerating && !_generationCancellation!.IsCancellationRequested;
    }

    private static void SelectComboBoxItemByTag(WpfComboBox comboBox, string? value)
    {
        var selected = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), value?.Trim(), StringComparison.Ordinal));
        comboBox.SelectedItem = selected ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private static string GetSelectedTag(WpfComboBox comboBox, string fallback)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()?.Trim() is { Length: > 0 } value
            ? value
            : fallback;
    }

    private static (int Width, int Height) BuildGenerationDimensions(ImageGenerationProfile profile, string aspectRatio)
    {
        const int minimumDimension = 256;
        const int maximumDimension = 4096;
        var longEdge = NormalizeGenerationDimension(Math.Max(profile.DefaultWidth, profile.DefaultHeight), maximumDimension);
        var (width, height) = aspectRatio switch
        {
            "16:9" => (longEdge, longEdge * 9 / 16),
            "9:16" => (longEdge * 9 / 16, longEdge),
            "4:3" => (longEdge, longEdge * 3 / 4),
            "3:4" => (longEdge * 3 / 4, longEdge),
            _ => (longEdge, longEdge)
        };

        return (
            NormalizeGenerationDimension(Math.Max(width, minimumDimension), maximumDimension),
            NormalizeGenerationDimension(Math.Max(height, minimumDimension), maximumDimension));
    }

    private static int NormalizeGenerationDimension(int value, int maximum)
    {
        var normalized = Math.Clamp(value, 256, maximum);
        return normalized - (normalized % 8);
    }

    private void RenderNodes()
    {
        CanvasSurface.Children.Clear();
        _nodeCards.Clear();
        if (_document is null)
        {
            return;
        }

        CanvasSurface.Children.Add(_centerGridReference);
        Canvas.SetZIndex(_centerGridReference, -1000);

        var assets = _workspace.Project.Assets.ToDictionary(static asset => asset.Id, StringComparer.Ordinal);
        foreach (var node in _document.AssetNodes.OrderBy(static item => item.ZIndex))
        {
            var card = CreateNodeCard(node, assets.GetValueOrDefault(node.AssetId));
            _nodeCards[node.Id] = card;
            CanvasSurface.Children.Add(card);
            PositionNodeCard(node, card);
        }

        CanvasSurface.Children.Add(_selectionRectangle);
        Canvas.SetZIndex(_selectionRectangle, int.MaxValue);
        UpdateSelectionVisuals();
        UpdateNavigator();
    }

    private void InitializeCanvasGuides()
    {
        var guideBrush = TryFindResource("Theme.Brush.TextMuted") as WpfBrush ?? WpfBrushes.Gray;
        var originBrush = TryFindResource("Theme.Brush.AccentBorder") as WpfBrush ?? WpfBrushes.DodgerBlue;
        var majorGridBrush = TryFindResource("Theme.Brush.TextSecondary") as WpfBrush ?? WpfBrushes.LightGray;

        _centerGridReference.Width = CanvasSurface.Width;
        _centerGridReference.Height = CanvasSurface.Height;

        for (var coordinate = 0d; coordinate <= _centerGridReference.Width; coordinate += 100d)
        {
            var isMajorLine = Math.Abs(coordinate % 500d) < 0.1d;
            _centerGridReference.Children.Add(new WpfLine
            {
                X1 = coordinate,
                Y1 = 0d,
                X2 = coordinate,
                Y2 = _centerGridReference.Height,
                Stroke = isMajorLine ? majorGridBrush : guideBrush,
                StrokeThickness = isMajorLine ? 1d : 0.5d,
                Opacity = isMajorLine ? 0.2d : 0.1d
            });
        }

        for (var coordinate = 0d; coordinate <= _centerGridReference.Height; coordinate += 100d)
        {
            var isMajorLine = Math.Abs(coordinate % 500d) < 0.1d;
            _centerGridReference.Children.Add(new WpfLine
            {
                X1 = 0d,
                Y1 = coordinate,
                X2 = _centerGridReference.Width,
                Y2 = coordinate,
                Stroke = isMajorLine ? majorGridBrush : guideBrush,
                StrokeThickness = isMajorLine ? 1d : 0.5d,
                Opacity = isMajorLine ? 0.2d : 0.1d
            });
        }

        _centerGridReference.Children.Add(new WpfLine
        {
            X1 = _centerGridReference.Width / 2d,
            Y1 = 0d,
            X2 = _centerGridReference.Width / 2d,
            Y2 = _centerGridReference.Height,
            Stroke = originBrush,
            StrokeThickness = 1.25d,
            Opacity = 0.45d
        });
        _centerGridReference.Children.Add(new WpfLine
        {
            X1 = 0d,
            Y1 = _centerGridReference.Height / 2d,
            X2 = _centerGridReference.Width,
            Y2 = _centerGridReference.Height / 2d,
            Stroke = originBrush,
            StrokeThickness = 1.25d,
            Opacity = 0.45d
        });
        Canvas.SetLeft(_centerGridReference, 0d);
        Canvas.SetTop(_centerGridReference, 0d);

        _selectionRectangle.Stroke = TryFindResource("Theme.Brush.AccentBorder") as WpfBrush ?? WpfBrushes.DodgerBlue;
        _selectionRectangle.StrokeThickness = 1d;
        _selectionRectangle.StrokeDashArray = new DoubleCollection([3d, 2d]);
        _selectionRectangle.Fill = new SolidColorBrush(WpfColor.FromArgb(28, 70, 160, 240));
    }

    private void UpdateSelectionRectangle(WpfPoint currentWorld)
    {
        var selection = CreateNormalizedRect(_selectionStartWorld, currentWorld);
        Canvas.SetLeft(_selectionRectangle, selection.X);
        Canvas.SetTop(_selectionRectangle, selection.Y);
        _selectionRectangle.Width = Math.Max(1d, selection.Width);
        _selectionRectangle.Height = Math.Max(1d, selection.Height);
        _selectionRectangle.Visibility = Visibility.Visible;

        if (_document is null)
        {
            return;
        }

        _selectedNodeIds.Clear();
        foreach (var node in _document.AssetNodes)
        {
            var nodeBounds = new WpfRect(node.X, node.Y, node.Width, node.Height);
            if (selection.IntersectsWith(nodeBounds))
            {
                _selectedNodeIds.Add(node.Id);
            }
        }

        UpdateSelectionVisuals();
    }

    private static WpfRect CreateNormalizedRect(WpfPoint start, WpfPoint end)
    {
        return new WpfRect(
            Math.Min(start.X, end.X),
            Math.Min(start.Y, end.Y),
            Math.Abs(end.X - start.X),
            Math.Abs(end.Y - start.Y));
    }

    private WpfPoint ToCanvasWorldPoint(WpfPoint viewportPoint)
    {
        if (_document is null)
        {
            return viewportPoint;
        }

        return new WpfPoint(
            (viewportPoint.X - _document.ViewportOffsetX) / _document.ViewportScale,
            (viewportPoint.Y - _document.ViewportOffsetY) / _document.ViewportScale);
    }

    private void UpdateSelectionVisuals()
    {
        foreach (var (nodeId, card) in _nodeCards)
        {
            ApplyNodeSelectionAppearance(card, _selectedNodeIds.Contains(nodeId));
        }
    }

    private void ApplyNodeSelectionAppearance(Border card, bool isSelected)
    {
        card.SetResourceReference(BorderBrushProperty, isSelected ? "Theme.Brush.AccentBorder" : "Theme.Brush.ButtonBorder");
        card.BorderThickness = new Thickness(isSelected ? 2d : 1d);
    }

    private ContextMenu CreateNodeContextMenu(AiCanvasAssetNode node)
    {
        var menu = new ContextMenu
        {
            HasDropShadow = false,
            Style = (Style)FindResource("CanvasContextMenuStyle")
        };

        var removeFromCanvas = new MenuItem { Header = "从画布移除", Tag = node.Id };
        removeFromCanvas.Style = (Style)FindResource("CanvasContextMenuItemStyle");
        removeFromCanvas.Click += RemoveNodeFromCanvasMenuItem_OnClick;
        menu.Items.Add(removeFromCanvas);

        var separator = new Separator();
        separator.Style = (Style)FindResource("CanvasContextMenuSeparatorStyle");
        menu.Items.Add(separator);

        var moveToRecycleBin = new MenuItem { Header = "移至项目回收站", Tag = node.Id };
        moveToRecycleBin.Style = (Style)FindResource("CanvasContextMenuItemStyle");
        moveToRecycleBin.Click += MoveNodeAssetToRecycleBinMenuItem_OnClick;
        menu.Items.Add(moveToRecycleBin);
        return menu;
    }

    private async void RemoveNodeFromCanvasMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string nodeId } || _document is null)
        {
            return;
        }

        CloseOwningContextMenu((MenuItem)sender);

        if (_document.AssetNodes.RemoveAll(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal)) == 0)
        {
            return;
        }

        _selectedNodeIds.Remove(nodeId);
        RenderNodes();
        await SaveCanvasAsync("已从画布移除素材节点，项目素材保持不变。");
    }

    private async void MoveNodeAssetToRecycleBinMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string nodeId } || _document is null)
        {
            return;
        }

        CloseOwningContextMenu((MenuItem)sender);

        var node = _document.AssetNodes.FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        var asset = node is null
            ? null
            : _workspace.Project.Assets.FirstOrDefault(candidate => string.Equals(candidate.Id, node.AssetId, StringComparison.Ordinal));
        if (asset is null)
        {
            CanvasStatusTextBlock.Text = "该节点的项目素材已不存在，可直接选择“从画布移除”。";
            return;
        }

        if (!ConfirmDialogWindow.Confirm(
                this,
                "移至项目回收站",
                $"确定将素材“{asset.DisplayName}”移至项目回收站吗？画布中引用它的节点会一并移除，可在主菜单的项目回收站恢复素材。",
                "移至回收站"))
        {
            return;
        }

        var movedCount = await _projectWorkspaceService.MoveToRecycleBinAsync(_workspace, [asset.Id]);
        if (movedCount == 0)
        {
            CanvasStatusTextBlock.Text = "素材未能移至项目回收站。";
            return;
        }

        _document.AssetNodes.RemoveAll(candidate => string.Equals(candidate.AssetId, asset.Id, StringComparison.Ordinal));
        _selectedNodeIds.Clear();
        RenderNodes();
        RefreshReferenceAssets();
        await SaveCanvasAsync("素材已移至项目回收站，并从画布移除关联节点。");
    }

    private async Task SetNodeDisplaySizeAsync(string nodeId, bool useOriginalSize)
    {
        if (_document is null)
        {
            return;
        }

        var node = _document.AssetNodes.FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
        var asset = node is null
            ? null
            : _workspace.Project.Assets.FirstOrDefault(candidate => string.Equals(candidate.Id, node.AssetId, StringComparison.Ordinal));
        if (node is null || asset is null)
        {
            CanvasStatusTextBlock.Text = "找不到需要调整显示尺寸的画布素材。";
            return;
        }

        if (useOriginalSize)
        {
            var sourcePath = Path.Combine(_workspace.DirectoryPath, asset.RelativePath);
            if (!TryGetImageDimensions(sourcePath, out var width, out var height))
            {
                CanvasStatusTextBlock.Text = "无法读取素材原始尺寸。";
                return;
            }

            node.Width = Math.Clamp(width, 96d, 4096d);
            node.Height = Math.Clamp(height, 72d, 4096d);
        }
        else
        {
            node.Width = 240d;
            node.Height = 180d;
        }

        node.UseOriginalSize = useOriginalSize;
        RenderNodes();
        await SaveCanvasAsync(useOriginalSize ? "已切换为原始尺寸显示。" : "已切换为标准卡片尺寸。" );
    }

    private static bool TryGetImageDimensions(string sourcePath, out double width, out double height)
    {
        width = 0d;
        height = 0d;
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        try
        {
            var bitmap = LoadBitmap(sourcePath);
            width = bitmap.PixelWidth;
            height = bitmap.PixelHeight;
            return width > 0d && height > 0d;
        }
        catch
        {
            return false;
        }
    }

    private Border CreateNodeCard(AiCanvasAssetNode node, ProjectAsset? asset)
    {
        var card = new Border
        {
            Width = node.Width,
            Height = node.Height,
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(14),
            Cursor = WpfCursors.SizeAll,
            Tag = node
        };
        card.SetResourceReference(BackgroundProperty, "Theme.Brush.SurfaceBackground");
        ApplyNodeSelectionAppearance(card, _selectedNodeIds.Contains(node.Id));
        card.MouseLeftButtonDown += NodeCard_OnMouseLeftButtonDown;
        card.ContextMenu = CreateNodeContextMenu(node);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var previewFrame = new Border
        {
            CornerRadius = new CornerRadius(9),
            ClipToBounds = true
        };
        previewFrame.SetResourceReference(BackgroundProperty, "Theme.Brush.SurfaceAltBackground");
        if (asset is not null)
        {
            var sourcePath = Path.Combine(_workspace.DirectoryPath, asset.RelativePath);
            if (File.Exists(sourcePath))
            {
                previewFrame.Child = new WpfImage
                {
                    Source = LoadBitmap(sourcePath),
                    Stretch = Stretch.UniformToFill,
                    SnapsToDevicePixels = true
                };
            }
        }

        if (previewFrame.Child is null)
        {
            previewFrame.Child = new TextBlock
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = TryFindResource("Theme.Brush.TextMuted") as WpfBrush,
                Text = asset is null ? "素材已不可用" : "无法读取预览"
            };
        }

        var label = new TextBlock
        {
            Margin = new Thickness(2, 8, 2, 0),
            FontWeight = FontWeights.SemiBold,
            Text = asset?.DisplayName ?? "已移除素材",
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetRow(previewFrame, 0);
        Grid.SetRow(label, 1);
        layout.Children.Add(previewFrame);
        layout.Children.Add(label);
        card.Child = layout;
        return card;
    }

    private static BitmapImage LoadBitmap(string sourcePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void NodeCard_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: AiCanvasAssetNode node })
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _ = SetNodeDisplaySizeAsync(node.Id, useOriginalSize: !node.UseOriginalSize);
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (!_selectedNodeIds.Add(node.Id))
            {
                _selectedNodeIds.Remove(node.Id);
            }
        }
        else if (!_selectedNodeIds.Contains(node.Id))
        {
            _selectedNodeIds.Clear();
            _selectedNodeIds.Add(node.Id);
        }

        UpdateSelectionVisuals();
        _draggedNode = node;
        _dragStart = e.GetPosition(CanvasViewport);
        _nodeStart = new WpfPoint(node.X, node.Y);
        _dragStartPositions.Clear();
        if (_document is not null)
        {
            foreach (var selected in _document.AssetNodes.Where(candidate => _selectedNodeIds.Contains(candidate.Id)))
            {
                _dragStartPositions[selected.Id] = new WpfPoint(selected.X, selected.Y);
            }
        }
        CanvasViewport.CaptureMouse();
        e.Handled = true;
    }

    private void CanvasViewport_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStart = e.GetPosition(CanvasViewport);
            _viewportStart = new WpfPoint(_document.ViewportOffsetX, _document.ViewportOffsetY);
            CanvasViewport.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                _selectedNodeIds.Clear();
                UpdateSelectionVisuals();
            }

            _isSelectingNodes = true;
            _selectionStartWorld = ToCanvasWorldPoint(e.GetPosition(CanvasViewport));
            UpdateSelectionRectangle(_selectionStartWorld);
            CanvasViewport.CaptureMouse();
            e.Handled = true;
        }
    }

    private void CanvasViewport_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        if (_draggedNode is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(CanvasViewport);
            var deltaX = (current.X - _dragStart.X) / _document.ViewportScale;
            var deltaY = (current.Y - _dragStart.Y) / _document.ViewportScale;
            foreach (var node in _document.AssetNodes.Where(candidate => _dragStartPositions.ContainsKey(candidate.Id)))
            {
                var start = _dragStartPositions[node.Id];
                node.X = start.X + deltaX;
                node.Y = start.Y + deltaY;
                if (_nodeCards.TryGetValue(node.Id, out var card))
                {
                    PositionNodeCard(node, card);
                }
            }

            UpdateNavigator();
            return;
        }

        if (_isSelectingNodes && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSelectionRectangle(ToCanvasWorldPoint(e.GetPosition(CanvasViewport)));
            return;
        }

        if (_isPanning && e.MiddleButton == MouseButtonState.Pressed)
        {
            var current = e.GetPosition(CanvasViewport);
            _document.ViewportOffsetX = _viewportStart.X + current.X - _panStart.X;
            _document.ViewportOffsetY = _viewportStart.Y + current.Y - _panStart.Y;
            ApplyViewport();
        }
    }

    private async void CanvasViewport_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelectingNodes)
        {
            _isSelectingNodes = false;
            _selectionRectangle.Visibility = Visibility.Collapsed;
            CanvasViewport.ReleaseMouseCapture();
            UpdateSelectionVisuals();
            return;
        }

        if (_draggedNode is null && !_isPanning)
        {
            return;
        }

        _draggedNode = null;
        _dragStartPositions.Clear();
        _isPanning = false;
        CanvasViewport.ReleaseMouseCapture();
        await SaveCanvasAsync("画布状态已自动保存。");
    }

    private void CanvasViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        var pointer = e.GetPosition(CanvasViewport);
        var previousScale = _document.ViewportScale;
        var nextScale = Math.Clamp(previousScale * (e.Delta > 0 ? 1.12d : 1d / 1.12d), 0.25d, 3d);
        var worldX = (pointer.X - _document.ViewportOffsetX) / previousScale;
        var worldY = (pointer.Y - _document.ViewportOffsetY) / previousScale;
        _document.ViewportScale = nextScale;
        _document.ViewportOffsetX = pointer.X - worldX * nextScale;
        _document.ViewportOffsetY = pointer.Y - worldY * nextScale;
        ApplyViewport();
        e.Handled = true;
    }

    private async void AddSelectedAssetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        var selectedAssetIds = GetSelectedReferenceAssetIds();
        if (selectedAssetIds.Count == 0)
        {
            CanvasStatusTextBlock.Text = "请先在右侧参考图中选择至少一张图片。";
            return;
        }

        _document = await _canvasService.AddAssetNodesAsync(_workspace, _document, selectedAssetIds);
        RenderNodes();
        RefreshReferenceAssets(selectedAssetIds);
        CanvasStatusTextBlock.Text = "已将选中参考图加入画布。";
    }

    private async void ImportReferenceAssetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Multiselect = true,
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|所有文件|*.*",
            InitialDirectory = _workspace.DirectoryPath
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await ImportReferenceFilesAsync(dialog.FileNames, "已导入参考图");
    }

    private async void Window_OnDrop(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(WpfDataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        await ImportReferenceFilesAsync(files, "已拖入参考图");
        e.Handled = true;
    }

    private void Window_OnDragOver(object sender, WpfDragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(WpfDataFormats.FileDrop)
            ? WpfDragDropEffects.Copy
            : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void QuickGenerationComposer_OnDragOver(object sender, WpfDragEventArgs e)
    {
        if (!e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            e.Handled = false;
            return;
        }

        e.Effects = WpfDragDropEffects.Copy;
        e.Handled = true;
    }

    private async void QuickGenerationComposer_OnDrop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData(WpfDataFormats.FileDrop) is not string[] files)
        {
            return;
        }

        await ImportReferenceFilesAsync(files, "已导入参考图");
        e.Handled = true;
    }

    private void CanvasViewport_OnDragOver(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetDataPresent(ReferenceAssetIdsDragFormat))
        {
            e.Effects = WpfDragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Handled = false;
    }

    private async void CanvasViewport_OnDrop(object sender, WpfDragEventArgs e)
    {
        if (_document is null || !e.Data.GetDataPresent(ReferenceAssetIdsDragFormat))
        {
            return;
        }

        if (e.Data.GetData(ReferenceAssetIdsDragFormat) is not string[] assetIds || assetIds.Length == 0)
        {
            return;
        }

        var dropPoint = ToCanvasWorldPoint(e.GetPosition(CanvasViewport));
        _document = await _canvasService.AddAssetNodesAsync(_workspace, _document, assetIds);
        for (var index = 0; index < assetIds.Length; index++)
        {
            var node = _document.AssetNodes.FirstOrDefault(candidate => string.Equals(candidate.AssetId, assetIds[index], StringComparison.Ordinal));
            if (node is null)
            {
                continue;
            }

            node.X = dropPoint.X + index * 28d;
            node.Y = dropPoint.Y + index * 28d;
            node.ZIndex = _document.AssetNodes.Count + index;
        }

        _selectedNodeIds.Clear();
        foreach (var assetId in assetIds)
        {
            var node = _document.AssetNodes.FirstOrDefault(candidate => string.Equals(candidate.AssetId, assetId, StringComparison.Ordinal));
            if (node is not null)
            {
                _selectedNodeIds.Add(node.Id);
            }
        }

        RenderNodes();
        RefreshReferenceAssets(assetIds);
        await SaveCanvasAsync("已将参考图拖入画布。");
        e.Handled = true;
    }

    private async void Window_OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.F
            && Keyboard.Modifiers == ModifierKeys.None
            && !IsTextEditingControl(e.OriginalSource as DependencyObject))
        {
            FocusCanvasTarget();
            await SaveCanvasAsync("画布视图已保存。" );
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V
            && Keyboard.Modifiers == ModifierKeys.Control
            && (System.Windows.Clipboard.ContainsFileDropList() || System.Windows.Clipboard.ContainsImage()))
        {
            await ImportClipboardReferencesAsync();
            e.Handled = true;
        }
    }

    private async Task ImportClipboardReferencesAsync()
    {
        if (System.Windows.Clipboard.ContainsFileDropList())
        {
            await ImportReferenceFilesAsync(System.Windows.Clipboard.GetFileDropList().Cast<string>(), "已从剪贴板导入参考图");
            return;
        }

        if (!System.Windows.Clipboard.ContainsImage() || System.Windows.Clipboard.GetImage() is not BitmapSource image)
        {
            CanvasStatusTextBlock.Text = "剪贴板中没有可导入的图片或图片文件。";
            return;
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"SnapCat-canvas-{Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            await ImportReferenceFilesAsync([temporaryPath], "已从剪贴板导入参考图");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsTextEditingControl(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is WpfTextBox or WpfComboBox)
            {
                return true;
            }
        }

        return false;
    }

    private async Task ImportReferenceFilesAsync(IEnumerable<string> sourcePaths, string successPrefix)
    {
        if (_document is null)
        {
            return;
        }

        var validPaths = sourcePaths
            .Where(IsSupportedImageFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (validPaths.Length == 0)
        {
            CanvasStatusTextBlock.Text = "没有找到可导入的图片文件。";
            return;
        }

        var importedAssetIds = new List<string>();
        var errors = new List<string>();
        foreach (var sourcePath in validPaths)
        {
            try
            {
                var asset = await _projectWorkspaceService.ImportImageAsync(
                    _workspace,
                    sourcePath,
                    ProjectAssetKind.Reference,
                    ProjectAssetCategory.StyleReference);
                importedAssetIds.Add(asset.Id);
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
        }

        if (importedAssetIds.Count > 0)
        {
            _document = await _canvasService.SetReferenceAssetsAsync(
                _workspace,
                _document,
                _document.ReferenceAssetIds.Concat(importedAssetIds));
            RefreshReferenceAssets(importedAssetIds);
        }

        CanvasStatusTextBlock.Text = errors.Count == 0
            ? $"{successPrefix}：{importedAssetIds.Count} 张。"
            : $"{successPrefix}：{importedAssetIds.Count} 张，{errors.Count} 张失败：{errors[0]}";
    }

    private void AddSelectedReferencesToCanvasButton_OnClick(object sender, RoutedEventArgs e)
    {
        AddSelectedAssetsButton_OnClick(sender, e);
    }

    private async void RemoveReferenceButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        var selectedAssetIds = GetSelectedReferenceAssetIds();
        if (selectedAssetIds.Count == 0)
        {
            CanvasStatusTextBlock.Text = "请先选择需要移除的参考图。";
            return;
        }

        _document = await _canvasService.SetReferenceAssetsAsync(
            _workspace,
            _document,
            _document.ReferenceAssetIds.Where(id => !selectedAssetIds.Contains(id, StringComparer.Ordinal)));
        RefreshReferenceAssets();
        CanvasStatusTextBlock.Text = "已从参考图顺序中移除选中图片，画布上的已有节点不会删除。";
    }

    private async void MoveReferenceUpButton_OnClick(object sender, RoutedEventArgs e)
    {
        await MoveSelectedReferenceAsync(-1);
    }

    private async void MoveReferenceDownButton_OnClick(object sender, RoutedEventArgs e)
    {
        await MoveSelectedReferenceAsync(1);
    }

    private async Task MoveSelectedReferenceAsync(int offset)
    {
        if (_document is null || ReferenceAssetsListBox.SelectedItem is not ProjectAssetListItem selected)
        {
            CanvasStatusTextBlock.Text = "请先选择一张需要调整顺序的参考图。";
            return;
        }

        var currentIndex = _document.ReferenceAssetIds.IndexOf(selected.Asset.Id);
        var targetIndex = currentIndex + offset;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= _document.ReferenceAssetIds.Count)
        {
            return;
        }

        (_document.ReferenceAssetIds[currentIndex], _document.ReferenceAssetIds[targetIndex]) =
            (_document.ReferenceAssetIds[targetIndex], _document.ReferenceAssetIds[currentIndex]);
        _document = await _canvasService.SetReferenceAssetsAsync(_workspace, _document, _document.ReferenceAssetIds);
        RefreshReferenceAssets([selected.Asset.Id]);
        CanvasStatusTextBlock.Text = "参考图顺序已保存。";
    }

    private void ReferenceAssetsListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ReferencePreviewImage.Source = (ReferenceAssetsListBox.SelectedItem as ProjectAssetListItem)?.Thumbnail;
        UpdateReferenceActionState();
    }

    private void ReferenceAssetsListBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _referenceDragStart = e.GetPosition(ReferenceAssetsListBox);
        _referenceDragItem = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext as ProjectAssetListItem;
    }

    private void ReferenceAssetsListBox_OnPreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_referenceDragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(ReferenceAssetsListBox);
        if (Math.Abs(currentPoint.X - _referenceDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _referenceDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var assetIds = GetSelectedReferenceAssetIds();
        if (assetIds.Count == 0 || !assetIds.Contains(_referenceDragItem.Asset.Id, StringComparer.Ordinal))
        {
            assetIds = [_referenceDragItem.Asset.Id];
        }

        _referenceDragItem = null;
        var data = new System.Windows.DataObject(ReferenceAssetIdsDragFormat, assetIds.ToArray());
        DragDrop.DoDragDrop(ReferenceAssetsListBox, data, WpfDragDropEffects.Copy);
    }

    private IReadOnlyList<string> GetSelectedReferenceAssetIds() => ReferenceAssetsListBox.SelectedItems
        .OfType<ProjectAssetListItem>()
        .Select(static item => item.Asset.Id)
        .ToArray();

    private void RefreshReferenceAssets(IEnumerable<string>? selectedAssetIds = null)
    {
        if (_document is null)
        {
            return;
        }

        var selectedIds = (selectedAssetIds ?? GetSelectedReferenceAssetIds()).ToHashSet(StringComparer.Ordinal);
        var assets = _workspace.Project.Assets.ToDictionary(static asset => asset.Id, StringComparer.Ordinal);
        var items = _document.ReferenceAssetIds
            .Where(assets.ContainsKey)
            .Select(id => assets[id])
            .Select((asset, index) => new ProjectAssetListItem(
                asset,
                Path.Combine(_workspace.DirectoryPath, asset.RelativePath),
                Path.Combine(_workspace.DirectoryPath, asset.ThumbnailRelativePath),
                index + 1))
            .ToList();
        ReferenceAssetsListBox.ItemsSource = items;
        foreach (var item in items.Where(item => selectedIds.Contains(item.Asset.Id)))
        {
            ReferenceAssetsListBox.SelectedItems.Add(item);
        }

        ReferencePreviewImage.Source = (ReferenceAssetsListBox.SelectedItem as ProjectAssetListItem)?.Thumbnail;
        UpdateReferenceActionState();
    }

    private void UpdateReferenceActionState()
    {
        var selectedCount = ReferenceAssetsListBox.SelectedItems.Count;
        RemoveReferenceButton.IsEnabled = selectedCount > 0;
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

    private static void CloseOwningContextMenu(MenuItem menuItem)
    {
        if (menuItem.Parent is ContextMenu menu)
        {
            menu.IsOpen = false;
        }
    }

    private static bool IsSupportedImageFile(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return false;
        }

        return Path.GetExtension(sourcePath).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";
    }

    private void ResetViewportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        _document.ViewportScale = 1d;
        _document.ViewportOffsetX = 0d;
        _document.ViewportOffsetY = 0d;
        ApplyViewport();
        CanvasStatusTextBlock.Text = "画布视图已还原。";
    }

    private async void SaveCanvasButton_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveCanvasAsync("画布已保存到项目目录。" );
    }

    private async Task SaveCanvasAsync(string successMessage)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            SaveGenerationDraftFromControls();
            await _canvasService.SaveAsync(_workspace, _document);
            CanvasStatusTextBlock.Text = successMessage;
        }
        catch (Exception exception)
        {
            CanvasStatusTextBlock.Text = $"保存画布失败：{exception.Message}";
        }
    }

    private void PositionNodeCard(AiCanvasAssetNode node, FrameworkElement card)
    {
        card.Width = node.Width;
        card.Height = node.Height;
        Canvas.SetLeft(card, node.X);
        Canvas.SetTop(card, node.Y);
        Canvas.SetZIndex(card, node.ZIndex);
    }

    private void ApplyViewport()
    {
        if (_document is null)
        {
            return;
        }

        CanvasScaleTransform.ScaleX = _document.ViewportScale;
        CanvasScaleTransform.ScaleY = _document.ViewportScale;
        CanvasTranslateTransform.X = _document.ViewportOffsetX;
        CanvasTranslateTransform.Y = _document.ViewportOffsetY;
        UpdateNavigator();
    }

    private void CanvasViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateNavigator();
    }

    private void UpdateNavigator()
    {
        if (_document is null
            || CanvasViewport.ActualWidth <= 0d
            || CanvasViewport.ActualHeight <= 0d)
        {
            CanvasNavigatorBorder.Visibility = Visibility.Collapsed;
            UpdateQuickGenerationComposerMargin(reserveNavigatorSpace: false);
            return;
        }

        var mapBounds = GetNavigatorMapBounds();
        var mapScale = Math.Min(
            CanvasNavigatorSurface.Width / Math.Max(mapBounds.Width, 1d),
            CanvasNavigatorSurface.Height / Math.Max(mapBounds.Height, 1d));
        CanvasNavigatorSurface.Children.Clear();
        var nodeBrush = TryFindResource("Theme.Brush.Accent") as WpfBrush ?? WpfBrushes.DodgerBlue;
        var frameBrush = TryFindResource("Theme.Brush.TextPrimary") as WpfBrush ?? WpfBrushes.White;
        foreach (var node in _document.AssetNodes)
        {
            var rectangle = new WpfRectangle
            {
                Width = Math.Max(3d, node.Width * mapScale),
                Height = Math.Max(3d, node.Height * mapScale),
                Fill = nodeBrush,
                Opacity = _selectedNodeIds.Contains(node.Id) ? 0.95d : 0.55d,
                RadiusX = 1d,
                RadiusY = 1d,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rectangle, (node.X - mapBounds.X) * mapScale);
            Canvas.SetTop(rectangle, (node.Y - mapBounds.Y) * mapScale);
            CanvasNavigatorSurface.Children.Add(rectangle);
        }

        var viewportBounds = GetViewportWorldBounds();
        var viewportFrame = new WpfRectangle
        {
            Width = Math.Max(4d, viewportBounds.Width * mapScale),
            Height = Math.Max(4d, viewportBounds.Height * mapScale),
            Stroke = frameBrush,
            StrokeThickness = 1d,
            Fill = WpfBrushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(viewportFrame, (viewportBounds.X - mapBounds.X) * mapScale);
        Canvas.SetTop(viewportFrame, (viewportBounds.Y - mapBounds.Y) * mapScale);
        CanvasNavigatorSurface.Children.Add(viewportFrame);
        CanvasNavigatorBorder.Visibility = Visibility.Visible;
        UpdateQuickGenerationComposerMargin(reserveNavigatorSpace: true);
    }

    private void UpdateQuickGenerationComposerMargin(bool reserveNavigatorSpace)
    {
        QuickGenerationComposer.Margin = reserveNavigatorSpace
            ? new Thickness(16d, 0d, 206d, 14d)
            : new Thickness(16d, 0d, 16d, 14d);
    }

    private void CanvasNavigatorSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_document is null || CanvasViewport.ActualWidth <= 0d || CanvasViewport.ActualHeight <= 0d)
        {
            return;
        }

        var mapBounds = GetNavigatorMapBounds();
        var mapScale = Math.Min(
            CanvasNavigatorSurface.Width / Math.Max(mapBounds.Width, 1d),
            CanvasNavigatorSurface.Height / Math.Max(mapBounds.Height, 1d));
        if (mapScale <= 0d)
        {
            return;
        }

        var pointer = e.GetPosition(CanvasNavigatorSurface);
        var worldX = mapBounds.X + Math.Clamp(pointer.X, 0d, CanvasNavigatorSurface.Width) / mapScale;
        var worldY = mapBounds.Y + Math.Clamp(pointer.Y, 0d, CanvasNavigatorSurface.Height) / mapScale;
        _document.ViewportOffsetX = CanvasViewport.ActualWidth / 2d - worldX * _document.ViewportScale;
        _document.ViewportOffsetY = CanvasViewport.ActualHeight / 2d - worldY * _document.ViewportScale;
        ApplyViewport();
        e.Handled = true;
    }

    private WpfRect GetViewportWorldBounds()
    {
        if (_document is null)
        {
            return WpfRect.Empty;
        }

        return new WpfRect(
            -_document.ViewportOffsetX / _document.ViewportScale,
            -_document.ViewportOffsetY / _document.ViewportScale,
            CanvasViewport.ActualWidth / _document.ViewportScale,
            CanvasViewport.ActualHeight / _document.ViewportScale);
    }

    private WpfRect GetNavigatorMapBounds()
    {
        var contentBounds = GetCanvasContentBounds();
        if (contentBounds.IsEmpty)
        {
            contentBounds = new WpfRect(
                CanvasSurface.Width / 2d - 600d,
                CanvasSurface.Height / 2d - 450d,
                1200d,
                900d);
        }

        var viewportBounds = GetViewportWorldBounds();
        var left = Math.Min(contentBounds.Left, viewportBounds.Left);
        var top = Math.Min(contentBounds.Top, viewportBounds.Top);
        var right = Math.Max(contentBounds.Right, viewportBounds.Right);
        var bottom = Math.Max(contentBounds.Bottom, viewportBounds.Bottom);
        const double padding = 70d;
        return new WpfRect(
            left - padding,
            top - padding,
            Math.Max(1d, right - left + padding * 2d),
            Math.Max(1d, bottom - top + padding * 2d));
    }

    private WpfRect GetCanvasContentBounds()
    {
        if (_document is null || _document.AssetNodes.Count == 0)
        {
            return WpfRect.Empty;
        }

        var left = _document.AssetNodes.Min(static node => node.X);
        var top = _document.AssetNodes.Min(static node => node.Y);
        var right = _document.AssetNodes.Max(static node => node.X + node.Width);
        var bottom = _document.AssetNodes.Max(static node => node.Y + node.Height);
        return new WpfRect(left, top, Math.Max(1d, right - left), Math.Max(1d, bottom - top));
    }

    private void FocusCanvasTarget()
    {
        if (_document is null)
        {
            return;
        }

        var selectedNodes = _document.AssetNodes.Where(node => _selectedNodeIds.Contains(node.Id)).ToArray();
        var bounds = selectedNodes.Length == 0
            ? GetCanvasContentBounds()
            : GetBounds(selectedNodes);
        if (bounds.IsEmpty)
        {
            CanvasStatusTextBlock.Text = "画布中还没有可定位的素材。";
            return;
        }

        var width = Math.Max(CanvasViewport.ActualWidth, 1d);
        var height = Math.Max(CanvasViewport.ActualHeight, 1d);
        const double viewportPadding = 72d;
        var scale = Math.Clamp(
            Math.Min((width - viewportPadding * 2d) / Math.Max(bounds.Width, 1d), (height - viewportPadding * 2d) / Math.Max(bounds.Height, 1d)),
            0.25d,
            3d);
        _document.ViewportScale = scale;
        _document.ViewportOffsetX = width / 2d - (bounds.X + bounds.Width / 2d) * scale;
        _document.ViewportOffsetY = height / 2d - (bounds.Y + bounds.Height / 2d) * scale;
        ApplyViewport();
        CanvasStatusTextBlock.Text = selectedNodes.Length == 0 ? "已定位到画布全部内容。" : $"已定位到 {selectedNodes.Length} 个选中对象。";
    }

    private static WpfRect GetBounds(IEnumerable<AiCanvasAssetNode> nodes)
    {
        var materialized = nodes.ToArray();
        if (materialized.Length == 0)
        {
            return WpfRect.Empty;
        }

        var left = materialized.Min(static node => node.X);
        var top = materialized.Min(static node => node.Y);
        var right = materialized.Max(static node => node.X + node.Width);
        var bottom = materialized.Max(static node => node.Y + node.Height);
        return new WpfRect(left, top, Math.Max(1d, right - left), Math.Max(1d, bottom - top));
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            e.Handled = true;
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Window_OnStateChanged(object? sender, EventArgs e)
    {
        MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void Window_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double collapseThreshold = 1180d;
        const double restoreThreshold = 1260d;
        if (ActualWidth < collapseThreshold && _isInspectorVisible)
        {
            _isInspectorAutoCollapsed = true;
            SetInspectorVisible(false);
            return;
        }

        if (ActualWidth >= restoreThreshold && _isInspectorAutoCollapsed)
        {
            _isInspectorAutoCollapsed = false;
            SetInspectorVisible(true);
        }
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _generationCancellation?.Cancel();
        if (_document is not null)
        {
            SaveGenerationDraftFromControls();
            _ = SaveCanvasOnCloseAsync(_document);
        }

        base.OnClosing(e);
    }

    private async Task SaveCanvasOnCloseAsync(AiCanvasDocument document)
    {
        try
        {
            await _canvasService.SaveAsync(_workspace, document);
        }
        catch
        {
            // A close action must never block the UI when a project drive becomes temporarily unavailable.
        }
    }
}
