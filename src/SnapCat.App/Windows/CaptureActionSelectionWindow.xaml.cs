using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SnapCat.App.Services;
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
    private const double DragPanelRefreshIntervalMs = 16;
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
    private readonly bool _usesWindowsTextExtractor;
    private double _moveDragAccumulatedX;
    private double _moveDragAccumulatedY;
    private MoveAxisLock _moveAxisLock;
    private DragInteractionKind _dragInteractionKind;
    private DateTime _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;
    private bool _keepVisibleForOcrActions;
    private TaskCompletionSource<bool>? _actionCompletionSource;

    public CaptureActionSelectionWindow(Int32Rect captureRegion, bool usesWindowsTextExtractor = false)
    {
        InitializeComponent();
        _usesWindowsTextExtractor = usesWindowsTextExtractor;
        _virtualScreenBounds = SystemInformation.VirtualScreen;
        _initialCaptureRegion = captureRegion;
        SelectedAction = CaptureActionKind.Cancel;
        SelectedRegion = captureRegion;
        Loaded += CaptureActionSelectionWindow_OnLoaded;
        Closed += (_, _) => _actionCompletionSource?.TrySetResult(false);
        ApplyOcrActionLabels();
    }

    public CaptureActionKind SelectedAction { get; private set; }

    public Int32Rect SelectedRegion { get; private set; }

    public Task<bool> ShowForActionSelectionAsync(bool keepVisibleForOcrActions)
    {
        _keepVisibleForOcrActions = keepVisibleForOcrActions;
        _actionCompletionSource = new TaskCompletionSource<bool>();
        Show();
        return _actionCompletionSource.Task;
    }

    private void ApplyOcrActionLabels()
    {
        if (!_usesWindowsTextExtractor)
        {
            return;
        }

        OcrOnlyButton.ToolTip = "OCR 识别并自动复制";
        OcrOnlyLabelTextBlock.Text = "OCR复制";
        OcrTranslateButton.ToolTip = "OCR 识别并自动复制后翻译";
    }

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
        _selectionRect = SnapLocalRect(_selectionRect);

        SelectionOutline.CacheMode = new BitmapCache();
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
            // 等待操作模式下，框外点击只吞掉事件，避免误触关闭选框和操作菜单。
            e.Handled = true;
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
        _selectionRect = SnapLocalRect(new WpfRect(localLeft, localTop, localRight - localLeft, localBottom - localTop));
        UpdateSelectionChrome();
    }


}
