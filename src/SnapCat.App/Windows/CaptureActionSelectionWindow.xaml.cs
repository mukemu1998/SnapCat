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
    private double _moveDragAccumulatedX;
    private double _moveDragAccumulatedY;
    private MoveAxisLock _moveAxisLock;
    private DragInteractionKind _dragInteractionKind;
    private DateTime _lastAdjustmentPanelRefreshUtc = DateTime.MinValue;

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
        _selectionRect = new WpfRect(localLeft, localTop, localRight - localLeft, localBottom - localTop);
        UpdateSelectionChrome();
    }


}
