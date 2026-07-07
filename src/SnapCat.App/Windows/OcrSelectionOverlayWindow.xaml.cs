using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using FormsScreen = System.Windows.Forms.Screen;
using WpfCursor = System.Windows.Input.Cursor;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class OcrSelectionOverlayWindow : Window
{
    private const double EdgeThreshold = 10.0d;
    private const double MinSelectionSize = 28.0d;
    private const double TextSelectionDragThreshold = 4.0d;
    private static readonly TimeSpan RecognitionDebounceDelay = TimeSpan.FromMilliseconds(420);

    private readonly Int32Rect _initialRegion;
    private readonly Int32Rect _virtualScreenRegion;
    private readonly string _screenSnapshotPath;
    private readonly Func<Int32Rect, CancellationToken, Task<OcrResult>> _recognizeRegionAsync;
    private readonly Func<OcrResult, CancellationToken, Task>? _recognitionCompletedAsync;
    private readonly bool _showSelectionChrome;
    private readonly List<Border> _handles = [];
    private readonly List<TextRegionView> _textRegionViews = [];
    private readonly List<Border> _selectedTextHighlightBoxes = [];
    private readonly HashSet<int> _selectedRegionIndices = [];
    private CancellationTokenSource? _recognitionCts;
    private WpfRect _selectionRect = WpfRect.Empty;
    private WpfPoint _dragStartPoint;
    private WpfRect _dragStartSelection;
    private WpfPoint _textSelectionStartPoint;
    private DragMode _dragMode = DragMode.None;
    private bool _isDragging;
    private bool _isTextRegionSelectionPending;
    private bool _isSelectingTextRegions;
    private string _recognizedText = string.Empty;
    private IReadOnlyList<OcrTextRegion> _regions = Array.Empty<OcrTextRegion>();

    public OcrSelectionOverlayWindow(
        Int32Rect initialRegion,
        Int32Rect virtualScreenRegion,
        string screenSnapshotPath,
        Func<Int32Rect, CancellationToken, Task<OcrResult>> recognizeRegionAsync,
        bool showSelectionChrome = true,
        Func<OcrResult, CancellationToken, Task>? recognitionCompletedAsync = null)
    {
        InitializeComponent();
        _initialRegion = initialRegion;
        _virtualScreenRegion = virtualScreenRegion;
        _screenSnapshotPath = screenSnapshotPath;
        _recognizeRegionAsync = recognizeRegionAsync;
        _recognitionCompletedAsync = recognitionCompletedAsync;
        _showSelectionChrome = showSelectionChrome;

        Loaded += OcrSelectionOverlayWindow_OnLoaded;
        Closed += (_, _) => _recognitionCts?.Cancel();
        SizeChanged += (_, _) => RenderSelection();
    }

    public void BringToFrontForAdjustment()
    {
        if (!IsVisible)
        {
            Show();
        }

        Activate();
        Keyboard.Focus(RootCanvas);
    }

    private void OcrSelectionOverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionOverVirtualScreen();
        if (_showSelectionChrome)
        {
            LoadScreenSnapshot();
        }

        _selectionRect = RegionToLocalRect(_initialRegion);
        if (_showSelectionChrome)
        {
            EnsureHandles();
        }

        RenderSelection();
        Activate();
        Keyboard.Focus(RootCanvas);
        ScheduleRecognition(TimeSpan.Zero);
    }

    private void PositionOverVirtualScreen()
    {
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new WpfPoint(_virtualScreenRegion.X, _virtualScreenRegion.Y));
        var bottomRight = fromDevice.Transform(new WpfPoint(
            _virtualScreenRegion.X + _virtualScreenRegion.Width,
            _virtualScreenRegion.Y + _virtualScreenRegion.Height));

        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(24, bottomRight.X - topLeft.X);
        Height = Math.Max(24, bottomRight.Y - topLeft.Y);
    }

    private void LoadScreenSnapshot()
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(_screenSnapshotPath);
        bitmap.EndInit();
        bitmap.Freeze();
        ScreenImage.Source = bitmap;
    }

    private WpfRect RegionToLocalRect(Int32Rect region)
    {
        var source = PresentationSource.FromVisual(this);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = fromDevice.Transform(new WpfPoint(
            region.X - _virtualScreenRegion.X,
            region.Y - _virtualScreenRegion.Y));
        var bottomRight = fromDevice.Transform(new WpfPoint(
            region.X + region.Width - _virtualScreenRegion.X,
            region.Y + region.Height - _virtualScreenRegion.Y));

        return new WpfRect(topLeft, bottomRight);
    }

    private Int32Rect LocalRectToRegion(WpfRect rect)
    {
        var source = PresentationSource.FromVisual(this);
        var toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var normalized = NormalizeRect(rect);
        var topLeft = toDevice.Transform(new WpfPoint(normalized.Left, normalized.Top));
        var bottomRight = toDevice.Transform(new WpfPoint(normalized.Right, normalized.Bottom));

        return new Int32Rect(
            _virtualScreenRegion.X + (int)Math.Round(topLeft.X),
            _virtualScreenRegion.Y + (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
    }

    private void EnsureHandles()
    {
        if (_handles.Count > 0)
        {
            return;
        }

        for (var index = 0; index < 8; index++)
        {
            var handle = new Border
            {
                Width = 12,
                Height = 12,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            };
            handle.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.TextPrimary");
            handle.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.AccentBorder");
            _handles.Add(handle);
            RootCanvas.Children.Add(handle);
        }
    }

    private void RenderSelection()
    {
        if (ActualWidth <= 0
            || ActualHeight <= 0
            || !double.IsFinite(ActualWidth)
            || !double.IsFinite(ActualHeight))
        {
            return;
        }

        ScreenImage.Width = ActualWidth;
        ScreenImage.Height = ActualHeight;
        if (_selectionRect.IsEmpty || !IsFiniteRect(_selectionRect))
        {
            return;
        }

        var rect = ConstrainRect(NormalizeRect(_selectionRect));
        if (rect.IsEmpty || !IsFiniteRect(rect))
        {
            return;
        }

        _selectionRect = rect;

        if (_showSelectionChrome)
        {
            RenderDims(rect);
            RenderFrame(rect);
            RenderHandles(rect);
        }
        else
        {
            HideSelectionChrome();
        }

        RenderStatus(rect);
        RenderToolbar(rect);
        RenderRecognizedText(rect);
    }

    private void HideSelectionChrome()
    {
        DimTop.Visibility = Visibility.Collapsed;
        DimLeft.Visibility = Visibility.Collapsed;
        DimRight.Visibility = Visibility.Collapsed;
        DimBottom.Visibility = Visibility.Collapsed;
        SelectionFrame.Visibility = Visibility.Collapsed;
        ScreenImage.Visibility = Visibility.Collapsed;

        foreach (var handle in _handles)
        {
            handle.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderDims(WpfRect rect)
    {
        SetRect(DimTop, 0, 0, ActualWidth, rect.Top);
        SetRect(DimLeft, 0, rect.Top, rect.Left, rect.Height);
        SetRect(DimRight, rect.Right, rect.Top, Math.Max(0, ActualWidth - rect.Right), rect.Height);
        SetRect(DimBottom, 0, rect.Bottom, ActualWidth, Math.Max(0, ActualHeight - rect.Bottom));
    }

    private void RenderFrame(WpfRect rect)
    {
        SetRect(SelectionFrame, rect.Left, rect.Top, rect.Width, rect.Height);
    }

    private void RenderHandles(WpfRect rect)
    {
        if (_handles.Count != 8)
        {
            return;
        }

        var half = _handles[0].Width / 2.0d;
        SetHandle(_handles[0], rect.Left - half, rect.Top - half);
        SetHandle(_handles[1], rect.Left + (rect.Width / 2.0d) - half, rect.Top - half);
        SetHandle(_handles[2], rect.Right - half, rect.Top - half);
        SetHandle(_handles[3], rect.Left - half, rect.Top + (rect.Height / 2.0d) - half);
        SetHandle(_handles[4], rect.Right - half, rect.Top + (rect.Height / 2.0d) - half);
        SetHandle(_handles[5], rect.Left - half, rect.Bottom - half);
        SetHandle(_handles[6], rect.Left + (rect.Width / 2.0d) - half, rect.Bottom - half);
        SetHandle(_handles[7], rect.Right - half, rect.Bottom - half);
    }

    private void RenderStatus(WpfRect rect)
    {
        StatusBadge.UpdateLayout();
        var gap = 8.0d;
        var left = rect.Left;
        var top = rect.Bottom + gap;

        if (top + StatusBadge.ActualHeight > ActualHeight - 4)
        {
            top = rect.Top - StatusBadge.ActualHeight - gap;
        }

        if (top < 4)
        {
            top = Math.Clamp(rect.Top + gap, 4, Math.Max(4, ActualHeight - StatusBadge.ActualHeight - 4));
            left = rect.Right + gap;
        }

        if (left + StatusBadge.ActualWidth > ActualWidth - 4)
        {
            left = rect.Right - StatusBadge.ActualWidth;
        }

        left = Math.Clamp(left, 4, Math.Max(4, ActualWidth - StatusBadge.ActualWidth - 4));
        top = Math.Clamp(top, 4, Math.Max(4, ActualHeight - StatusBadge.ActualHeight - 4));
        Canvas.SetLeft(StatusBadge, left);
        Canvas.SetTop(StatusBadge, top);
    }

    private void RenderToolbar(WpfRect rect)
    {
        MiniToolbar.Visibility = string.IsNullOrWhiteSpace(_recognizedText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (MiniToolbar.Visibility != Visibility.Visible)
        {
            return;
        }

        MiniToolbar.UpdateLayout();
        var left = rect.Right + 8;
        var top = rect.Top + (rect.Height / 2.0d) - (MiniToolbar.ActualHeight / 2.0d);

        if (left + MiniToolbar.ActualWidth > ActualWidth - 8)
        {
            left = rect.Right - MiniToolbar.ActualWidth - 8;
        }

        Canvas.SetLeft(MiniToolbar, Math.Clamp(left, 4, Math.Max(4, ActualWidth - MiniToolbar.ActualWidth - 4)));
        Canvas.SetTop(MiniToolbar, Math.Clamp(top, 4, Math.Max(4, ActualHeight - MiniToolbar.ActualHeight - 4)));
    }

    private void RenderRecognizedText(WpfRect selectionRect)
    {
        TextLayer.Children.Clear();
        _textRegionViews.Clear();
        _selectedTextHighlightBoxes.Clear();
        _selectedRegionIndices.RemoveWhere(index => index < 0 || index >= _regions.Count);

        if (string.IsNullOrWhiteSpace(_recognizedText))
        {
            _selectedRegionIndices.Clear();
            return;
        }

        var selectionRegion = LocalRectToRegion(selectionRect);
        var regions = _regions.Count > 0
            ? _regions
            : EstimateTextRegions(selectionRegion);
        var scaleX = selectionRect.Width / Math.Max(1, selectionRegion.Width);
        var scaleY = selectionRect.Height / Math.Max(1, selectionRegion.Height);

        var displayRects = new List<WpfRect>();
        var index = 0;
        foreach (var region in regions.Where(static item => !string.IsNullOrWhiteSpace(item.Text)))
        {
            var left = selectionRect.Left + (region.X * scaleX);
            var top = selectionRect.Top + (region.Y * scaleY);
            var width = Math.Max(24, region.Width * scaleX);
            var height = Math.Max(12, region.Height * scaleY);
            var rect = new WpfRect(
                Math.Clamp(left, selectionRect.Left, selectionRect.Right - 1),
                Math.Clamp(top, selectionRect.Top, selectionRect.Bottom - 1),
                Math.Min(width, Math.Max(1, selectionRect.Right - left)),
                Math.Min(height, Math.Max(1, selectionRect.Bottom - top)));

            displayRects.Add(rect);
            var box = CreateTextRegionBox(isSelected: _selectedRegionIndices.Contains(index));
            SetRect(box, rect.Left, rect.Top, rect.Width, rect.Height);
            TextLayer.Children.Add(box);
            _textRegionViews.Add(new TextRegionView(index, region.Text, rect, box));
            index++;
        }

        foreach (var rect in MergeDisplayRectsByLine(displayRects))
        {
            var box = CreateTextDisplayLineBox();
            SetRect(box, rect.Left, rect.Top, rect.Width, rect.Height);
            TextLayer.Children.Insert(0, box);
        }

        RenderSelectedTextHighlights();
    }

    private Border CreateTextDisplayLineBox()
    {
        var box = new Border
        {
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(1),
            Opacity = 0.13d,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        box.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.AccentSoft");
        box.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.AccentBorder");
        return box;
    }

    private Border CreateTextRegionBox(bool isSelected)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(0),
            Opacity = 0,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
    }

    private Border CreateSelectedTextHighlightBox()
    {
        var box = new Border
        {
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(0),
            Opacity = 0.30d,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        };
        box.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.Accent");
        return box;
    }

    private static IReadOnlyList<WpfRect> MergeDisplayRectsByLine(IReadOnlyList<WpfRect> rects)
    {
        if (rects.Count == 0)
        {
            return Array.Empty<WpfRect>();
        }

        var lines = new List<List<WpfRect>>();
        foreach (var rect in rects.OrderBy(rect => rect.Top).ThenBy(rect => rect.Left))
        {
            var centerY = rect.Top + (rect.Height / 2.0d);
            var line = lines.FirstOrDefault(item =>
            {
                var lineCenter = item.Average(existing => existing.Top + (existing.Height / 2.0d));
                var lineHeight = Math.Max(1.0d, item.Average(existing => existing.Height));
                return Math.Abs(centerY - lineCenter) <= lineHeight * 0.7d;
            });

            if (line is null)
            {
                lines.Add([rect]);
            }
            else
            {
                line.Add(rect);
            }
        }

        return lines.Select(line =>
        {
            var left = line.Min(rect => rect.Left);
            var top = line.Min(rect => rect.Top);
            var right = line.Max(rect => rect.Right);
            var bottom = line.Max(rect => rect.Bottom);
            return new WpfRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }).ToList();
    }

    private IReadOnlyList<OcrTextRegion> EstimateTextRegions(Int32Rect selectionRegion)
    {
        var lines = _recognizedText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .ToList();
        if (lines.Count == 0)
        {
            return Array.Empty<OcrTextRegion>();
        }

        var lineHeight = Math.Clamp(selectionRegion.Height / Math.Max(8.0d, lines.Count * 3.0d), 16.0d, 30.0d);
        var top = Math.Max(4.0d, selectionRegion.Height - ((lineHeight + 6.0d) * lines.Count) - 6.0d);
        var result = new List<OcrTextRegion>();

        foreach (var line in lines)
        {
            var width = Math.Clamp(line.Length * lineHeight * 0.65d, 64.0d, Math.Max(64.0d, selectionRegion.Width - 12.0d));
            result.Add(new OcrTextRegion(line, 6, top, width, lineHeight));
            top += lineHeight + 6.0d;
        }

        return result;
    }

    private async void ScheduleRecognition(TimeSpan delay)
    {
        _recognitionCts?.Cancel();
        _recognitionCts = new CancellationTokenSource();
        var token = _recognitionCts.Token;

        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }

            await RecognizeCurrentSelectionAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RecognizeCurrentSelectionAsync(CancellationToken cancellationToken)
    {
        var region = LocalRectToRegion(_selectionRect);
        _recognizedText = string.Empty;
        _regions = Array.Empty<OcrTextRegion>();
        StatusTextBlock.Text = "识别中...";
        SelectAllMenuItem.IsEnabled = false;
        CopyMenuItem.IsEnabled = false;
        RenderSelection();

        var result = await _recognizeRegionAsync(region, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            _recognizedText = result.Text.Trim();
            _regions = result.Regions;
            StatusTextBlock.Text = "OCR 完成，Esc 退出。";
            SelectAllMenuItem.IsEnabled = true;
            CopyMenuItem.IsEnabled = true;
        }
        else
        {
            _recognizedText = string.Empty;
            _regions = Array.Empty<OcrTextRegion>();
            StatusTextBlock.Text = "未识别到文本，调整选框会自动重试。";
            SelectAllMenuItem.IsEnabled = false;
            CopyMenuItem.IsEnabled = false;
        }

        RenderSelection();

        if (_recognitionCompletedAsync is not null)
        {
            await _recognitionCompletedAsync(result, cancellationToken);
        }
    }

    private void RootCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(RootCanvas);
        if (!string.IsNullOrWhiteSpace(_recognizedText) && IsTextRegionPoint(point) && !IsNearSelectionEdge(point))
        {
            BeginTextRegionSelectionPending(point);
            e.Handled = true;
            return;
        }

        var mode = HitTestSelection(point);
        if (mode == DragMode.None)
        {
            return;
        }

        _dragMode = mode;
        _isDragging = true;
        _dragStartPoint = point;
        _dragStartSelection = _selectionRect;
        RootCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void RootCanvas_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        var point = e.GetPosition(RootCanvas);
        if (_isTextRegionSelectionPending)
        {
            if (Distance(_textSelectionStartPoint, point) >= TextSelectionDragThreshold)
            {
                BeginTextRegionSelection();
                UpdateTextRegionSelection(point);
            }

            e.Handled = true;
            return;
        }

        if (_isSelectingTextRegions)
        {
            UpdateTextRegionSelection(point);
            e.Handled = true;
            return;
        }

        if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            ApplyDrag(point);
            e.Handled = true;
            return;
        }

        if (!string.IsNullOrWhiteSpace(_recognizedText) && IsTextRegionPoint(point) && !IsNearSelectionEdge(point))
        {
            Cursor = WpfCursors.IBeam;
            return;
        }

        Cursor = CursorForDragMode(HitTestSelection(point));
    }

    private void RootCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isTextRegionSelectionPending)
        {
            CancelTextRegionSelectionPending();
            e.Handled = true;
            return;
        }

        if (_isSelectingTextRegions)
        {
            CompleteTextRegionSelection(e.GetPosition(RootCanvas));
            e.Handled = true;
            return;
        }

        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        _dragMode = DragMode.None;
        RootCanvas.ReleaseMouseCapture();
        ScheduleRecognition(RecognitionDebounceDelay);
        e.Handled = true;
    }

    private void RootCanvas_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_recognizedText))
        {
            OcrContextMenu.IsOpen = true;
        }

        e.Handled = true;
    }

    private void ApplyDrag(WpfPoint point)
    {
        var dx = point.X - _dragStartPoint.X;
        var dy = point.Y - _dragStartPoint.Y;
        var rect = _dragStartSelection;

        switch (_dragMode)
        {
            case DragMode.Move:
                rect.Offset(dx, dy);
                break;
            case DragMode.Left:
                rect.X += dx;
                rect.Width -= dx;
                break;
            case DragMode.Right:
                rect.Width += dx;
                break;
            case DragMode.Top:
                rect.Y += dy;
                rect.Height -= dy;
                break;
            case DragMode.Bottom:
                rect.Height += dy;
                break;
            case DragMode.TopLeft:
                rect.X += dx;
                rect.Width -= dx;
                rect.Y += dy;
                rect.Height -= dy;
                break;
            case DragMode.TopRight:
                rect.Width += dx;
                rect.Y += dy;
                rect.Height -= dy;
                break;
            case DragMode.BottomLeft:
                rect.X += dx;
                rect.Width -= dx;
                rect.Height += dy;
                break;
            case DragMode.BottomRight:
                rect.Width += dx;
                rect.Height += dy;
                break;
        }

        _selectionRect = ConstrainRect(NormalizeRect(rect));
        _recognizedText = string.Empty;
        _regions = Array.Empty<OcrTextRegion>();
        _selectedRegionIndices.Clear();
        StatusTextBlock.Text = "调整选框中...";
        RenderSelection();
    }

    private bool IsTextRegionPoint(WpfPoint point)
    {
        return _textRegionViews.Any(view => view.Rect.Contains(point));
    }

    private bool IsNearSelectionEdge(WpfPoint point)
    {
        var rect = NormalizeRect(_selectionRect);
        var nearLeft = Math.Abs(point.X - rect.Left) <= EdgeThreshold;
        var nearRight = Math.Abs(point.X - rect.Right) <= EdgeThreshold;
        var nearTop = Math.Abs(point.Y - rect.Top) <= EdgeThreshold;
        var nearBottom = Math.Abs(point.Y - rect.Bottom) <= EdgeThreshold;
        return nearLeft || nearRight || nearTop || nearBottom;
    }

    private void BeginTextRegionSelectionPending(WpfPoint point)
    {
        _isTextRegionSelectionPending = true;
        _textSelectionStartPoint = point;
        RootCanvas.CaptureMouse();
    }

    private void CancelTextRegionSelectionPending()
    {
        _isTextRegionSelectionPending = false;
        if (RootCanvas.IsMouseCaptured)
        {
            RootCanvas.ReleaseMouseCapture();
        }

        if (_selectedRegionIndices.Count > 0)
        {
            ClearTextRegionSelection();
            StatusTextBlock.Text = "OCR 完成，Esc 退出。";
        }
    }

    private void BeginTextRegionSelection()
    {
        _isTextRegionSelectionPending = false;
        _isSelectingTextRegions = true;
        _selectedRegionIndices.Clear();
        UpdateTextRegionSelectionVisuals();
    }

    private void UpdateTextRegionSelection(WpfPoint point)
    {
        var rect = NormalizeRect(new WpfRect(_textSelectionStartPoint, point));

        _selectedRegionIndices.Clear();
        foreach (var view in _textRegionViews.Where(view => view.Rect.IntersectsWith(rect)))
        {
            _selectedRegionIndices.Add(view.Index);
        }

        UpdateTextRegionSelectionVisuals();
    }

    private void CompleteTextRegionSelection(WpfPoint point)
    {
        UpdateTextRegionSelection(point);
        _isSelectingTextRegions = false;
        RootCanvas.ReleaseMouseCapture();

        StatusTextBlock.Text = _selectedRegionIndices.Count > 0
            ? $"已选择 {_selectedRegionIndices.Count} 个识别区域。"
            : "OCR 完成，Esc 退出。";
    }

    private static double Distance(WpfPoint first, WpfPoint second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private void UpdateTextRegionSelectionVisuals()
    {
        foreach (var view in _textRegionViews)
        {
            view.Box.Opacity = 0;
            view.Box.BorderThickness = new Thickness(0);
        }

        RenderSelectedTextHighlights();
    }

    private void ClearTextRegionSelection()
    {
        _selectedRegionIndices.Clear();
        UpdateTextRegionSelectionVisuals();
    }

    private void RenderSelectedTextHighlights()
    {
        foreach (var box in _selectedTextHighlightBoxes)
        {
            TextLayer.Children.Remove(box);
        }

        _selectedTextHighlightBoxes.Clear();

        if (_selectedRegionIndices.Count == 0)
        {
            return;
        }

        var selectedRects = _textRegionViews
            .Where(view => _selectedRegionIndices.Contains(view.Index))
            .Select(view => view.Rect)
            .ToList();

        foreach (var rect in MergeDisplayRectsByLine(selectedRects).Select(InflateTextSelectionRect))
        {
            var box = CreateSelectedTextHighlightBox();
            SetRect(box, rect.Left, rect.Top, rect.Width, rect.Height);
            TextLayer.Children.Add(box);
            _selectedTextHighlightBoxes.Add(box);
        }
    }

    private static WpfRect InflateTextSelectionRect(WpfRect rect)
    {
        var inflated = rect;
        inflated.Inflate(1.5d, 1.0d);
        return inflated;
    }

    private DragMode HitTestSelection(WpfPoint point)
    {
        var rect = NormalizeRect(_selectionRect);
        var nearLeft = Math.Abs(point.X - rect.Left) <= EdgeThreshold;
        var nearRight = Math.Abs(point.X - rect.Right) <= EdgeThreshold;
        var nearTop = Math.Abs(point.Y - rect.Top) <= EdgeThreshold;
        var nearBottom = Math.Abs(point.Y - rect.Bottom) <= EdgeThreshold;
        var insideX = point.X >= rect.Left - EdgeThreshold && point.X <= rect.Right + EdgeThreshold;
        var insideY = point.Y >= rect.Top - EdgeThreshold && point.Y <= rect.Bottom + EdgeThreshold;

        if (nearLeft && nearTop)
        {
            return DragMode.TopLeft;
        }

        if (nearRight && nearTop)
        {
            return DragMode.TopRight;
        }

        if (nearLeft && nearBottom)
        {
            return DragMode.BottomLeft;
        }

        if (nearRight && nearBottom)
        {
            return DragMode.BottomRight;
        }

        if (nearLeft && insideY)
        {
            return DragMode.Left;
        }

        if (nearRight && insideY)
        {
            return DragMode.Right;
        }

        if (nearTop && insideX)
        {
            return DragMode.Top;
        }

        if (nearBottom && insideX)
        {
            return DragMode.Bottom;
        }

        return rect.Contains(point) ? DragMode.Move : DragMode.None;
    }

    private static WpfCursor CursorForDragMode(DragMode mode)
    {
        return mode switch
        {
            DragMode.Left or DragMode.Right => WpfCursors.SizeWE,
            DragMode.Top or DragMode.Bottom => WpfCursors.SizeNS,
            DragMode.TopLeft or DragMode.BottomRight => WpfCursors.SizeNWSE,
            DragMode.TopRight or DragMode.BottomLeft => WpfCursors.SizeNESW,
            DragMode.Move => WpfCursors.SizeAll,
            _ => WpfCursors.Arrow
        };
    }

    private WpfRect ConstrainRect(WpfRect rect)
    {
        if (rect.IsEmpty || !IsFiniteRect(rect))
        {
            return WpfRect.Empty;
        }

        rect.Width = Math.Max(MinSelectionSize, rect.Width);
        rect.Height = Math.Max(MinSelectionSize, rect.Height);

        if (rect.Left < 0)
        {
            rect.X = 0;
        }

        if (rect.Top < 0)
        {
            rect.Y = 0;
        }

        if (rect.Right > ActualWidth)
        {
            rect.X = Math.Max(0, ActualWidth - rect.Width);
        }

        if (rect.Bottom > ActualHeight)
        {
            rect.Y = Math.Max(0, ActualHeight - rect.Height);
        }

        return rect;
    }

    private static WpfRect NormalizeRect(WpfRect rect)
    {
        if (rect.IsEmpty || !IsFiniteRect(rect))
        {
            return WpfRect.Empty;
        }

        return new WpfRect(
            Math.Min(rect.Left, rect.Right),
            Math.Min(rect.Top, rect.Bottom),
            Math.Abs(rect.Width),
            Math.Abs(rect.Height));
    }

    private static void SetRect(FrameworkElement element, double left, double top, double width, double height)
    {
        if (!double.IsFinite(left)
            || !double.IsFinite(top)
            || !double.IsFinite(width)
            || !double.IsFinite(height))
        {
            return;
        }

        element.Width = Math.Max(0, width);
        element.Height = Math.Max(0, height);
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
    }

    private static void SetHandle(Border handle, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        Canvas.SetLeft(handle, x);
        Canvas.SetTop(handle, y);
    }

    private static bool IsFiniteRect(WpfRect rect)
    {
        return double.IsFinite(rect.Left)
            && double.IsFinite(rect.Top)
            && double.IsFinite(rect.Width)
            && double.IsFinite(rect.Height)
            && double.IsFinite(rect.Right)
            && double.IsFinite(rect.Bottom);
    }

    private void SelectAllMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        SelectAllRecognizedText();
    }

    private void CopyMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CopyRecognizedText();
    }

    private void SelectAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        SelectAllRecognizedText();
    }

    private void CopyAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        CopyRecognizedText();
    }

    private void SelectAllRecognizedText()
    {
        _selectedRegionIndices.Clear();
        foreach (var view in _textRegionViews)
        {
            _selectedRegionIndices.Add(view.Index);
        }

        UpdateTextRegionSelectionVisuals();
        StatusTextBlock.Text = _selectedRegionIndices.Count > 0
            ? $"已全选 {_selectedRegionIndices.Count} 个识别区域。"
            : "OCR 完成，Esc 退出。";
    }

    private void CopyRecognizedText()
    {
        if (string.IsNullOrWhiteSpace(_recognizedText))
        {
            return;
        }

        Clipboard.SetText(GetCopyText());
        StatusTextBlock.Text = "已复制识别文本。";
    }

    private string GetCopyText()
    {
        if (_selectedRegionIndices.Count == 0)
        {
            return _recognizedText;
        }

        var selectedText = _textRegionViews
            .Where(view => _selectedRegionIndices.Contains(view.Index))
            .OrderBy(view => view.Rect.Top)
            .ThenBy(view => view.Rect.Left)
            .ToList();

        return ComposeSelectedRegionText(selectedText);
    }

    private static string ComposeSelectedRegionText(IReadOnlyList<TextRegionView> selectedViews)
    {
        if (selectedViews.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<List<TextRegionView>>();
        foreach (var view in selectedViews)
        {
            var centerY = view.Rect.Top + (view.Rect.Height / 2.0d);
            var line = lines.FirstOrDefault(item =>
            {
                var lineCenter = item.Average(existing => existing.Rect.Top + (existing.Rect.Height / 2.0d));
                var lineHeight = Math.Max(1.0d, item.Average(existing => existing.Rect.Height));
                return Math.Abs(centerY - lineCenter) <= lineHeight * 0.65d;
            });

            if (line is null)
            {
                lines.Add([view]);
            }
            else
            {
                line.Add(view);
            }
        }

        var composedLines = lines
            .Select(line => ComposeSelectedLineText(line.OrderBy(view => view.Rect.Left).ToList()))
            .Where(static text => !string.IsNullOrWhiteSpace(text));

        return string.Join(Environment.NewLine, composedLines);
    }

    private static string ComposeSelectedLineText(IReadOnlyList<TextRegionView> line)
    {
        if (line.Count == 0)
        {
            return string.Empty;
        }

        var result = new System.Text.StringBuilder();
        TextRegionView? previous = null;
        foreach (var view in line)
        {
            var text = view.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (previous is not null && ShouldInsertSpace(previous, view))
            {
                result.Append(' ');
            }

            result.Append(text);
            previous = view;
        }

        return result.ToString();
    }

    private static bool ShouldInsertSpace(TextRegionView previous, TextRegionView current)
    {
        var gap = current.Rect.Left - previous.Rect.Right;
        var averageHeight = Math.Max(1.0d, (previous.Rect.Height + current.Rect.Height) / 2.0d);
        return gap > averageHeight * 0.35d;
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectAllRecognizedText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopyRecognizedText();
            e.Handled = true;
        }
    }

    private enum DragMode
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private sealed record TextRegionView(int Index, string Text, WpfRect Rect, Border Box);
}
