using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SnapCat.Core.Models;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace SnapCat.App.Windows;

public partial class CaptureActionSelectionWindow : Window
{
    private enum MoveAxisLock
    {
        None,
        Horizontal,
        Vertical
    }

    private enum DragInteractionKind
    {
        None,
        Move,
        Resize
    }

    private const double HandleSize = 12;
    private const double MinSelectionSize = 24;
    private const double ToolbarGap = 0;
    private static Int32Rect? s_lastSelectionRegion;

    private readonly Rectangle _virtualScreenBounds;
    private readonly Int32Rect _initialCaptureRegion;
    private WpfRect _selectionRect;
    private WpfRect _moveDragOriginRect;
    private WpfSize _cachedToolbarSize;
    private Matrix _fromDevice = Matrix.Identity;
    private Matrix _toDevice = Matrix.Identity;
    private bool _isApplyingBoundsInputs;
    private bool _isSelectionDragging;
    private double _moveDragAccumulatedX;
    private double _moveDragAccumulatedY;
    private MoveAxisLock _moveAxisLock;
    private DragInteractionKind _dragInteractionKind;
    private DateTime _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;
    private Effect? _toolbarEffectBackup;

    public CaptureActionSelectionWindow(Int32Rect captureRegion)
    {
        InitializeComponent();
        _virtualScreenBounds = SystemInformation.VirtualScreen;
        _initialCaptureRegion = captureRegion;
        SelectedAction = CaptureActionKind.Cancel;
        SelectedRegion = captureRegion;
        Loaded += CaptureActionSelectionWindow_OnLoaded;
    }

    public CaptureActionKind SelectedAction { get; private set; }

    public Int32Rect SelectedRegion { get; private set; }

    private void CaptureActionSelectionWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeTransforms();

        var topLeftDip = _fromDevice.Transform(new WpfPoint(_virtualScreenBounds.Left, _virtualScreenBounds.Top));
        var bottomRightDip = _fromDevice.Transform(new WpfPoint(_virtualScreenBounds.Right, _virtualScreenBounds.Bottom));

        Left = topLeftDip.X;
        Top = topLeftDip.Y;
        Width = bottomRightDip.X - topLeftDip.X;
        Height = bottomRightDip.Y - topLeftDip.Y;

        var selectionTopLeft = _fromDevice.Transform(new WpfPoint(_initialCaptureRegion.X, _initialCaptureRegion.Y));
        var selectionBottomRight = _fromDevice.Transform(new WpfPoint(
            _initialCaptureRegion.X + _initialCaptureRegion.Width,
            _initialCaptureRegion.Y + _initialCaptureRegion.Height));

        _selectionRect = new WpfRect(
            selectionTopLeft.X - Left,
            selectionTopLeft.Y - Top,
            selectionBottomRight.X - selectionTopLeft.X,
            selectionBottomRight.Y - selectionTopLeft.Y);

        SelectionOutline.CacheMode = new BitmapCache();
        ToolbarHost.CacheMode = new BitmapCache();
        TopLeftThumb.CacheMode = new BitmapCache();
        TopThumb.CacheMode = new BitmapCache();
        TopRightThumb.CacheMode = new BitmapCache();
        RightThumb.CacheMode = new BitmapCache();
        BottomRightThumb.CacheMode = new BitmapCache();
        BottomThumb.CacheMode = new BitmapCache();
        BottomLeftThumb.CacheMode = new BitmapCache();
        LeftThumb.CacheMode = new BitmapCache();
        _cachedToolbarSize = MeasureToolbarSize();
        UpdateSelectionChrome(forceToolbarMeasure: true, forcePanelRefresh: true);
    }

    private void RootCanvas_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, RootCanvas))
        {
            SelectedAction = CaptureActionKind.Cancel;
            DialogResult = false;
        }
    }

    private void MoveThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _moveDragOriginRect = _selectionRect;
        _moveDragAccumulatedX = 0;
        _moveDragAccumulatedY = 0;
        _moveAxisLock = MoveAxisLock.None;
        BeginSelectionDrag(DragInteractionKind.Move);
    }

    private void MoveThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        _moveDragAccumulatedX += e.HorizontalChange;
        _moveDragAccumulatedY += e.VerticalChange;

        var horizontalChange = _moveDragAccumulatedX;
        var verticalChange = _moveDragAccumulatedY;
        var shiftPressed =
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift)
            == System.Windows.Input.ModifierKeys.Shift;

        if (shiftPressed)
        {
            if (_moveAxisLock == MoveAxisLock.None
                && (Math.Abs(horizontalChange) > 0.5 || Math.Abs(verticalChange) > 0.5))
            {
                _moveAxisLock = Math.Abs(horizontalChange) >= Math.Abs(verticalChange)
                    ? MoveAxisLock.Horizontal
                    : MoveAxisLock.Vertical;
            }

            if (_moveAxisLock == MoveAxisLock.Horizontal)
            {
                verticalChange = 0;
            }
            else if (_moveAxisLock == MoveAxisLock.Vertical)
            {
                horizontalChange = 0;
            }
        }
        else
        {
            _moveAxisLock = MoveAxisLock.None;
        }

        var movementBounds = GetMovementBounds(_selectionRect.Width, _selectionRect.Height);
        var newX = Math.Round(Clamp(_moveDragOriginRect.X + horizontalChange, movementBounds.Left, movementBounds.Right));
        var newY = Math.Round(Clamp(_moveDragOriginRect.Y + verticalChange, movementBounds.Top, movementBounds.Bottom));
        _selectionRect = new WpfRect(newX, newY, _moveDragOriginRect.Width, _moveDragOriginRect.Height);
        UpdateSelectionChrome();
    }

    private void MoveThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _moveDragOriginRect = _selectionRect;
        _moveDragAccumulatedX = 0;
        _moveDragAccumulatedY = 0;
        _moveAxisLock = MoveAxisLock.None;
        EndSelectionDrag();
    }

    private void ResizeThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginSelectionDrag(DragInteractionKind.Resize);
    }

    private void ResizeThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag)
        {
            return;
        }

        var left = _selectionRect.Left;
        var top = _selectionRect.Top;
        var right = _selectionRect.Right;
        var bottom = _selectionRect.Bottom;
        var originalRect = _selectionRect;

        switch (tag)
        {
            case "Left":
            case "TopLeft":
            case "BottomLeft":
                left = Clamp(left + e.HorizontalChange, 0, right - MinSelectionSize);
                break;
        }

        switch (tag)
        {
            case "Right":
            case "TopRight":
            case "BottomRight":
                right = Clamp(right + e.HorizontalChange, left + MinSelectionSize, RootCanvas.ActualWidth);
                break;
        }

        switch (tag)
        {
            case "Top":
            case "TopLeft":
            case "TopRight":
                top = Clamp(top + e.VerticalChange, 0, bottom - MinSelectionSize);
                break;
        }

        switch (tag)
        {
            case "Bottom":
            case "BottomLeft":
            case "BottomRight":
                bottom = Clamp(bottom + e.VerticalChange, top + MinSelectionSize, RootCanvas.ActualHeight);
                break;
        }

        if (LockAspectRatioCheckBox.IsChecked == true)
        {
            ApplyAspectRatioConstraint(tag, originalRect, ref left, ref top, ref right, ref bottom, e.HorizontalChange, e.VerticalChange);
        }

        ConstrainSelectionToAllowedBounds(ref left, ref top, ref right, ref bottom);
        left = Math.Round(left);
        top = Math.Round(top);
        right = Math.Round(right);
        bottom = Math.Round(bottom);

        _selectionRect = new WpfRect(new WpfPoint(left, top), new WpfPoint(right, bottom));
        UpdateSelectionChrome();
    }

    private void ResizeThumb_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndSelectionDrag();
    }

    private void UpdateSelectionChrome(bool forceToolbarMeasure = false, bool forcePanelRefresh = false)
    {
        Canvas.SetLeft(SelectionOutline, _selectionRect.X);
        Canvas.SetTop(SelectionOutline, _selectionRect.Y);
        SelectionOutline.Width = _selectionRect.Width;
        SelectionOutline.Height = _selectionRect.Height;

        Canvas.SetLeft(MoveThumb, _selectionRect.X);
        Canvas.SetTop(MoveThumb, _selectionRect.Y);
        MoveThumb.Width = _selectionRect.Width;
        MoveThumb.Height = _selectionRect.Height;

        PositionThumb(TopLeftThumb, _selectionRect.Left - HandleSize / 2, _selectionRect.Top - HandleSize / 2);
        PositionThumb(TopThumb, _selectionRect.Left + (_selectionRect.Width - HandleSize) / 2, _selectionRect.Top - HandleSize / 2);
        PositionThumb(TopRightThumb, _selectionRect.Right - HandleSize / 2, _selectionRect.Top - HandleSize / 2);
        PositionThumb(RightThumb, _selectionRect.Right - HandleSize / 2, _selectionRect.Top + (_selectionRect.Height - HandleSize) / 2);
        PositionThumb(BottomRightThumb, _selectionRect.Right - HandleSize / 2, _selectionRect.Bottom - HandleSize / 2);
        PositionThumb(BottomThumb, _selectionRect.Left + (_selectionRect.Width - HandleSize) / 2, _selectionRect.Bottom - HandleSize / 2);
        PositionThumb(BottomLeftThumb, _selectionRect.Left - HandleSize / 2, _selectionRect.Bottom - HandleSize / 2);
        PositionThumb(LeftThumb, _selectionRect.Left - HandleSize / 2, _selectionRect.Top + (_selectionRect.Height - HandleSize) / 2);

        if (_isSelectionDragging && _dragInteractionKind == DragInteractionKind.Move)
        {
            MoveThumb.Cursor = System.Windows.Input.Cursors.SizeAll;
            return;
        }

        if (forceToolbarMeasure || (_cachedToolbarSize.Width <= 0 || _cachedToolbarSize.Height <= 0))
        {
            _cachedToolbarSize = MeasureToolbarSize();
        }

        var toolbarSize = _cachedToolbarSize;
        var toolbarPosition = CalculateToolbarPosition(toolbarSize.Width, toolbarSize.Height);

        Canvas.SetLeft(ToolbarHost, toolbarPosition.X - Left);
        Canvas.SetTop(ToolbarHost, toolbarPosition.Y - Top);
        MoveThumb.Cursor = System.Windows.Input.Cursors.SizeAll;
        UpdateSelectionAdjustmentPanel(forcePanelRefresh);
        UpdateApplyPreviousBoundsButtonState();
    }

    private WpfPoint CalculateToolbarPosition(double toolbarWidth, double toolbarHeight)
    {
        var workArea = GetCurrentScreenWorkAreaLocalBounds();
        var maxX = Math.Max(workArea.Left, workArea.Right - toolbarWidth);
        var maxY = Math.Max(workArea.Top, workArea.Bottom - toolbarHeight);

        var centeredX = Clamp(
            _selectionRect.X + (_selectionRect.Width - toolbarWidth) / 2,
            workArea.Left,
            maxX);

        var centeredY = Clamp(
            _selectionRect.Y + (_selectionRect.Height - toolbarHeight) / 2,
            workArea.Top,
            maxY);

        var candidates = new[]
        {
            new WpfPoint(centeredX, _selectionRect.Bottom + ToolbarGap),
            new WpfPoint(centeredX, _selectionRect.Y - toolbarHeight - ToolbarGap),
            new WpfPoint(_selectionRect.Right + ToolbarGap, centeredY),
            new WpfPoint(_selectionRect.X - toolbarWidth - ToolbarGap, centeredY)
        };

        foreach (var candidate in candidates)
        {
            if (candidate.X >= workArea.Left
                && candidate.Y >= workArea.Top
                && candidate.X + toolbarWidth <= workArea.Right
                && candidate.Y + toolbarHeight <= workArea.Bottom)
            {
                return new WpfPoint(Left + candidate.X, Top + candidate.Y);
            }
        }

        var fallbackX = Clamp(centeredX, workArea.Left, maxX);
        var fallbackY = _selectionRect.Bottom + ToolbarGap <= workArea.Bottom - toolbarHeight
            ? _selectionRect.Bottom + ToolbarGap
            : _selectionRect.Y - toolbarHeight - ToolbarGap;

        return new WpfPoint(
            Left + fallbackX,
            Top + Clamp(fallbackY, workArea.Top, maxY));
    }

    private WpfRect GetCurrentScreenWorkAreaLocalBounds()
    {
        var selectedRegion = BuildSelectedRegion();
        var selectionBounds = new Rectangle(
            selectedRegion.X,
            selectedRegion.Y,
            Math.Max(1, selectedRegion.Width),
            Math.Max(1, selectedRegion.Height));

        var screen = FormsScreen.FromRectangle(selectionBounds);
        var workArea = screen.WorkingArea;

        var topLeftDip = _fromDevice.Transform(new WpfPoint(workArea.Left, workArea.Top));
        var bottomRightDip = _fromDevice.Transform(new WpfPoint(workArea.Right, workArea.Bottom));

        return new WpfRect(
            topLeftDip.X - Left,
            topLeftDip.Y - Top,
            bottomRightDip.X - topLeftDip.X,
            bottomRightDip.Y - topLeftDip.Y);
    }

    private void UpdateSelectionAdjustmentPanel(bool forceRefresh = false)
    {
        if (_isApplyingBoundsInputs)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (!forceRefresh && _isSelectionDragging && (now - _lastAdjustmentPanelRefreshUtc).TotalMilliseconds < 45)
        {
            return;
        }

        var selectedRegion = BuildSelectedRegion();
        var screen = FormsScreen.FromRectangle(new Rectangle(
            selectedRegion.X,
            selectedRegion.Y,
            Math.Max(1, selectedRegion.Width),
            Math.Max(1, selectedRegion.Height)));
        var workArea = screen.WorkingArea;
        var relativeX = selectedRegion.X - workArea.Left;
        var relativeY = selectedRegion.Y - workArea.Top;
        var ratio = selectedRegion.Height <= 0
            ? 0d
            : (double)selectedRegion.Width / selectedRegion.Height;

        SelectionInfoTextBlock.Text =
            $"{GetScreenDisplayLabel(screen)} | 绝对 X:{selectedRegion.X} Y:{selectedRegion.Y} W:{selectedRegion.Width} H:{selectedRegion.Height}";
        SelectionScreenInfoTextBlock.Text =
            $"屏幕内 X:{relativeX} Y:{relativeY} | 比例 {ratio:0.0000}";

        _isApplyingBoundsInputs = true;
        AbsoluteXTextBox.Text = selectedRegion.X.ToString(CultureInfo.InvariantCulture);
        AbsoluteYTextBox.Text = selectedRegion.Y.ToString(CultureInfo.InvariantCulture);
        WidthTextBox.Text = selectedRegion.Width.ToString(CultureInfo.InvariantCulture);
        HeightTextBox.Text = selectedRegion.Height.ToString(CultureInfo.InvariantCulture);
        _isApplyingBoundsInputs = false;
        _lastAdjustmentPanelRefreshUtc = now;
    }

    private void ApplyAspectRatioConstraint(
        string tag,
        WpfRect originalRect,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom,
        double horizontalChange,
        double verticalChange)
    {
        var ratio = originalRect.Height <= 0 ? 1d : originalRect.Width / originalRect.Height;
        if (ratio <= 0)
        {
            return;
        }

        var widthDriven = tag is "Left" or "Right"
            || ((tag.Contains("Left", StringComparison.Ordinal) || tag.Contains("Right", StringComparison.Ordinal))
                && Math.Abs(horizontalChange) >= Math.Abs(verticalChange));

        if (widthDriven)
        {
            var newWidth = Math.Max(MinSelectionSize, right - left);
            var newHeight = Math.Max(MinSelectionSize, newWidth / ratio);

            if (tag.Contains("Top", StringComparison.Ordinal))
            {
                top = bottom - newHeight;
            }
            else if (tag.Contains("Bottom", StringComparison.Ordinal))
            {
                bottom = top + newHeight;
            }
            else
            {
                var centerY = originalRect.Top + originalRect.Height / 2;
                top = centerY - newHeight / 2;
                bottom = centerY + newHeight / 2;
            }
        }
        else
        {
            var newHeight = Math.Max(MinSelectionSize, bottom - top);
            var newWidth = Math.Max(MinSelectionSize, newHeight * ratio);

            if (tag.Contains("Left", StringComparison.Ordinal))
            {
                left = right - newWidth;
            }
            else if (tag.Contains("Right", StringComparison.Ordinal))
            {
                right = left + newWidth;
            }
            else
            {
                var centerX = originalRect.Left + originalRect.Width / 2;
                left = centerX - newWidth / 2;
                right = centerX + newWidth / 2;
            }
        }

        left = Clamp(left, 0, RootCanvas.ActualWidth - MinSelectionSize);
        top = Clamp(top, 0, RootCanvas.ActualHeight - MinSelectionSize);
        right = Clamp(right, left + MinSelectionSize, RootCanvas.ActualWidth);
        bottom = Clamp(bottom, top + MinSelectionSize, RootCanvas.ActualHeight);

        var constrainedWidth = right - left;
        var constrainedHeight = Math.Max(MinSelectionSize, constrainedWidth / ratio);
        if (constrainedHeight > RootCanvas.ActualHeight)
        {
            constrainedHeight = RootCanvas.ActualHeight;
            constrainedWidth = Math.Max(MinSelectionSize, constrainedHeight * ratio);
        }

        if (tag.Contains("Top", StringComparison.Ordinal))
        {
            top = bottom - constrainedHeight;
        }
        else
        {
            bottom = top + constrainedHeight;
        }

        if (tag.Contains("Left", StringComparison.Ordinal))
        {
            left = right - constrainedWidth;
        }
        else if (tag.Contains("Right", StringComparison.Ordinal))
        {
            right = left + constrainedWidth;
        }

        left = Clamp(left, 0, RootCanvas.ActualWidth - constrainedWidth);
        top = Clamp(top, 0, RootCanvas.ActualHeight - constrainedHeight);
        right = left + constrainedWidth;
        bottom = top + constrainedHeight;
    }

    private void BoundsTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            ApplyBoundsFromInputs();
        }
    }

    private void ApplyBoundsFromInputs()
    {
        if (!TryParseBoundsInputs(out var x, out var y, out var width, out var height))
        {
            return;
        }

        var current = BuildSelectedRegion();
        var ratio = current.Height <= 0 ? 1d : (double)current.Width / current.Height;

        if (LockAspectRatioCheckBox.IsChecked == true)
        {
            var widthChanged = width != current.Width;
            var heightChanged = height != current.Height;
            if (widthChanged && !heightChanged)
            {
                height = Math.Max((int)MinSelectionSize, (int)Math.Round(width / ratio));
            }
            else if (!widthChanged && heightChanged)
            {
                width = Math.Max((int)MinSelectionSize, (int)Math.Round(height * ratio));
            }
            else if (widthChanged)
            {
                height = Math.Max((int)MinSelectionSize, (int)Math.Round(width / ratio));
            }
        }

        width = Math.Max((int)MinSelectionSize, width);
        height = Math.Max((int)MinSelectionSize, height);

        var bottomRightX = x + width;
        var bottomRightY = y + height;
        var topLeftDip = _fromDevice.Transform(new WpfPoint(x, y));
        var bottomRightDip = _fromDevice.Transform(new WpfPoint(bottomRightX, bottomRightY));

        var localLeft = topLeftDip.X - Left;
        var localTop = topLeftDip.Y - Top;
        var localRight = bottomRightDip.X - Left;
        var localBottom = bottomRightDip.Y - Top;

        ConstrainSelectionToAllowedBounds(
            ref localLeft,
            ref localTop,
            ref localRight,
            ref localBottom);

        _selectionRect = new WpfRect(localLeft, localTop, localRight - localLeft, localBottom - localTop);
        UpdateSelectionChrome();
    }

    private bool TryParseBoundsInputs(out int x, out int y, out int width, out int height)
    {
        x = 0;
        y = 0;
        width = 0;
        height = 0;

        var success =
            int.TryParse(AbsoluteXTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
            && int.TryParse(AbsoluteYTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out y)
            && int.TryParse(WidthTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out width)
            && int.TryParse(HeightTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out height);

        if (success)
        {
            return true;
        }

        SelectionScreenInfoTextBlock.Text = "请输入有效的整数坐标和尺寸后再应用。";
        return false;
    }

    private void ActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag)
        {
            SelectedAction = CaptureActionKind.Cancel;
            DialogResult = false;
            return;
        }

        SelectedAction = Enum.Parse<CaptureActionKind>(tag);
        if (SelectedAction == CaptureActionKind.Cancel)
        {
            DialogResult = false;
            return;
        }

        SelectedRegion = BuildSelectedRegion();
        s_lastSelectionRegion = SelectedRegion;
        DialogResult = true;
    }

    private void ApplyPreviousBoundsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (s_lastSelectionRegion is not { } previousRegion)
        {
            SelectionScreenInfoTextBlock.Text = "当前还没有可应用的上一次线框数据。";
            UpdateApplyPreviousBoundsButtonState();
            return;
        }

        ApplySelectionRegion(previousRegion);
    }

    private void UpdateApplyPreviousBoundsButtonState()
    {
        ApplyPreviousBoundsButton.IsEnabled = s_lastSelectionRegion.HasValue;
    }

    private void Window_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            SelectedAction = CaptureActionKind.Cancel;
            DialogResult = false;
        }
    }

    private Int32Rect BuildSelectedRegion()
    {
        var topLeftPx = _toDevice.Transform(new WpfPoint(_selectionRect.X + Left, _selectionRect.Y + Top));
        var bottomRightPx = _toDevice.Transform(new WpfPoint(_selectionRect.Right + Left, _selectionRect.Bottom + Top));

        return new Int32Rect(
            (int)Math.Round(topLeftPx.X),
            (int)Math.Round(topLeftPx.Y),
            (int)Math.Round(bottomRightPx.X - topLeftPx.X),
            (int)Math.Round(bottomRightPx.Y - topLeftPx.Y));
    }

    private void InitializeTransforms()
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not null)
        {
            _fromDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformFromDevice;
            _toDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformToDevice;
        }
    }

    private static void PositionThumb(FrameworkElement thumb, double x, double y)
    {
        Canvas.SetLeft(thumb, x);
        Canvas.SetTop(thumb, y);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private static int Clamp(int value, int min, int max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private FormsScreen GetSelectedScreen()
    {
        var selectedRegion = BuildSelectedRegion();
        var selectionBounds = new Rectangle(
            selectedRegion.X,
            selectedRegion.Y,
            Math.Max(1, selectedRegion.Width),
            Math.Max(1, selectedRegion.Height));

        return FormsScreen.FromRectangle(selectionBounds);
    }

    private void ApplySelectionRegion(Int32Rect region)
    {
        var width = Math.Max((int)MinSelectionSize, region.Width);
        var height = Math.Max((int)MinSelectionSize, region.Height);
        var topLeftDip = _fromDevice.Transform(new WpfPoint(region.X, region.Y));
        var bottomRightDip = _fromDevice.Transform(new WpfPoint(region.X + width, region.Y + height));

        var localLeft = topLeftDip.X - Left;
        var localTop = topLeftDip.Y - Top;
        var localRight = bottomRightDip.X - Left;
        var localBottom = bottomRightDip.Y - Top;

        ConstrainSelectionToAllowedBounds(ref localLeft, ref localTop, ref localRight, ref localBottom);
        _selectionRect = new WpfRect(localLeft, localTop, localRight - localLeft, localBottom - localTop);
        UpdateSelectionChrome();
    }

    private WpfSize MeasureToolbarSize()
    {
        ToolbarHost.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        return ToolbarHost.DesiredSize;
    }

    private void BeginSelectionDrag(DragInteractionKind interactionKind)
    {
        if (_isSelectionDragging)
        {
            return;
        }

        _isSelectionDragging = true;
        _dragInteractionKind = interactionKind;
        _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;
        _cachedToolbarSize = MeasureToolbarSize();

        if (ToolbarHost.Effect is not null)
        {
            _toolbarEffectBackup = ToolbarHost.Effect;
            ToolbarHost.Effect = null;
        }

        if (interactionKind == DragInteractionKind.Move)
        {
            ToolbarHost.Visibility = Visibility.Hidden;
        }
    }

    private void EndSelectionDrag()
    {
        if (!_isSelectionDragging)
        {
            return;
        }

        _isSelectionDragging = false;
        _dragInteractionKind = DragInteractionKind.None;
        if (ToolbarHost.Effect is null && _toolbarEffectBackup is not null)
        {
            ToolbarHost.Effect = _toolbarEffectBackup;
        }

        ToolbarHost.Visibility = Visibility.Visible;

        _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;
        UpdateSelectionChrome(forceToolbarMeasure: true, forcePanelRefresh: true);
    }

    private WpfRect GetMovementBounds(double width, double height)
    {
        var allowedBounds = new WpfRect(0, 0, RootCanvas.ActualWidth, RootCanvas.ActualHeight);
        return new WpfRect(
            allowedBounds.Left,
            allowedBounds.Top,
            Math.Max(0, allowedBounds.Width - width),
            Math.Max(0, allowedBounds.Height - height));
    }

    private void ConstrainSelectionToAllowedBounds(ref double left, ref double top, ref double right, ref double bottom)
    {
        var allowedBounds = new WpfRect(0, 0, RootCanvas.ActualWidth, RootCanvas.ActualHeight);
        var width = Math.Max(MinSelectionSize, right - left);
        var height = Math.Max(MinSelectionSize, bottom - top);

        width = Math.Min(width, allowedBounds.Width);
        height = Math.Min(height, allowedBounds.Height);

        left = Clamp(left, allowedBounds.Left, allowedBounds.Right - width);
        top = Clamp(top, allowedBounds.Top, allowedBounds.Bottom - height);
        right = left + width;
        bottom = top + height;
    }

    private static string GetScreenDisplayLabel(FormsScreen screen)
    {
        var index = Array.FindIndex(FormsScreen.AllScreens, candidate =>
            string.Equals(candidate.DeviceName, screen.DeviceName, StringComparison.OrdinalIgnoreCase));
        var screenNumber = index >= 0 ? index + 1 : 1;
        return $"屏幕 {screenNumber} ({screen.DeviceName})";
    }
}
