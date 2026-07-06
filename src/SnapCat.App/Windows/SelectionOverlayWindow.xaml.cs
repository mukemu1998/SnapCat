using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using SnapCat.App.Services;
using Clipboard = System.Windows.Clipboard;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using Point = System.Windows.Point;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using QueryCursorEventArgs = System.Windows.Input.QueryCursorEventArgs;
using WpfCursors = System.Windows.Input.Cursors;
using WpfMouse = System.Windows.Input.Mouse;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow : Window
{
    private const int MagnifierSourceSize = 28;
    private const int InspectorOffset = 14;
    private const double PreviewDragThreshold = 5;
    private const int ScreenEdgeSnapThreshold = 10;
    private const int WindowEdgeSnapThreshold = 10;
    private static readonly TimeSpan ColorInspectorContentRefreshInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SmartPreviewRefreshInterval = TimeSpan.FromMilliseconds(16);

    private readonly DrawingRectangle _virtualScreenBounds;
    private Point? _startPoint;
    private Point? _previewClickStartPoint;
    private Rect _selectionRect = Rect.Empty;
    private Int32Rect? _smartPreviewRegion;
    private Int32Rect? _pendingPreviewRegion;
    private Matrix _fromDevice = Matrix.Identity;
    private Matrix _toDevice = Matrix.Identity;
    private DrawingBitmap? _screenBitmap;
    private BitmapSource? _screenBitmapSource;
    private DrawingColor _currentColor = DrawingColor.Black;
    private DrawingPoint _currentScreenPoint;
    private DateTime _lastColorInspectorContentUpdateUtc = DateTime.MinValue;
    private Point _lastRenderCursorPoint;
    private Point _lastSmartPreviewPoint;
    private DateTime _lastSmartPreviewUpdateUtc = DateTime.MinValue;
    private bool _showHexColor;
    private bool _rightClickCancelPending;
    private int _smartPreviewQueryVersion;
    private bool _smartPreviewQueryInFlight;
    private DrawingPoint? _pendingAutomationPreviewPoint;
    private readonly object _automationPreviewCacheLock = new();
    private IReadOnlyList<AutomationPreviewCandidate> _automationPreviewCache = Array.Empty<AutomationPreviewCandidate>();
    private CancellationTokenSource? _automationPreviewCacheCts;
    private bool _automationPreviewCacheReady;
    private bool _automationPreviewCacheBuilding;

    public SelectionOverlayWindow()
    {
        InitializeComponent();

        _virtualScreenBounds = SystemInformation.VirtualScreen;
        Loaded += SelectionOverlayWindow_OnLoaded;
        QueryCursor += SelectionOverlayWindow_OnQueryCursor;
    }

    public Int32Rect? SelectedRegion { get; private set; }

    private void SelectionOverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        WpfMouse.OverrideCursor = WpfCursors.Cross;
        Cursor = WpfCursors.Cross;
        RootSurface.Cursor = WpfCursors.Cross;
        ForceCrossCursor();

        InitializeTransforms();
        CaptureVirtualScreenSnapshot();

        var topLeftDip = _fromDevice.Transform(new Point(_virtualScreenBounds.Left, _virtualScreenBounds.Top));
        var bottomRightDip = _fromDevice.Transform(new Point(_virtualScreenBounds.Right, _virtualScreenBounds.Bottom));

        Left = topLeftDip.X;
        Top = topLeftDip.Y;
        Width = bottomRightDip.X - topLeftDip.X;
        Height = bottomRightDip.Y - topLeftDip.Y;
        UpdateColorInspector(GetCursorLocalPoint());
        StartAutomationPreviewCacheBuild();
        CompositionTarget.Rendering += CompositionTarget_OnRendering;
    }

    private void CompositionTarget_OnRendering(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        var localPoint = GetCursorLocalPoint();
        if (localPoint.X < 0 || localPoint.Y < 0 || localPoint.X > ActualWidth || localPoint.Y > ActualHeight)
        {
            return;
        }

        _lastRenderCursorPoint = localPoint;
        UpdateColorInspector(localPoint);
    }

    private static void SelectionOverlayWindow_OnQueryCursor(object sender, QueryCursorEventArgs e)
    {
        e.Cursor = WpfCursors.Cross;
        e.Handled = true;
        ForceCrossCursor();
    }

    private void RootSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_smartPreviewRegion is not null)
        {
            _previewClickStartPoint = e.GetPosition(RootSurface);
            _pendingPreviewRegion = _smartPreviewRegion;
            RootSurface.CaptureMouse();
            return;
        }

        _startPoint = e.GetPosition(RootSurface);
        RootSurface.CaptureMouse();
        UpdateSelection(_startPoint.Value, _startPoint.Value);
    }

    private void RootSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(RootSurface);
        PositionColorInspector(current);

        if (_previewClickStartPoint is not null)
        {
            if (Distance(_previewClickStartPoint.Value, current) <= PreviewDragThreshold)
            {
                return;
            }

            _startPoint = _previewClickStartPoint;
            _previewClickStartPoint = null;
            _pendingPreviewRegion = null;
            OverlayLayer.ClearSmartPreview();
            UpdateSelection(_startPoint.Value, _startPoint.Value);
        }

        if (_startPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateSmartPreviewThrottled(current);
            return;
        }

        UpdateSelection(_startPoint.Value, current);
    }

    private void RootSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_previewClickStartPoint is not null && _pendingPreviewRegion is not null)
        {
            RootSurface.ReleaseMouseCapture();
            SelectedRegion = _pendingPreviewRegion.Value;
            DialogResult = true;
            return;
        }

        if (_startPoint is null)
        {
            return;
        }

        var endPoint = e.GetPosition(RootSurface);
        RootSurface.ReleaseMouseCapture();
        UpdateSelection(_startPoint.Value, endPoint);

        var left = _selectionRect.Left;
        var top = _selectionRect.Top;
        var width = _selectionRect.Width;
        var height = _selectionRect.Height;

        _startPoint = null;
        _previewClickStartPoint = null;
        _pendingPreviewRegion = null;
        OverlayLayer.ClearSelection();

        if (width < 8 || height < 8)
        {
            DialogResult = false;
            return;
        }

        var topLeftPx = _toDevice.Transform(new Point(left, top));
        var bottomRightPx = _toDevice.Transform(new Point(left + width, top + height));

        SelectedRegion = new Int32Rect(
            _virtualScreenBounds.Left + (int)Math.Round(topLeftPx.X),
            _virtualScreenBounds.Top + (int)Math.Round(topLeftPx.Y),
            (int)Math.Round(bottomRightPx.X - topLeftPx.X),
            (int)Math.Round(bottomRightPx.Y - topLeftPx.Y));

        DialogResult = true;
    }

    private void RootSurface_OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginRightClickCancel(e);
    }

    private void RootSurface_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        BeginRightClickCancel(e);
    }

    private void RootSurface_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteRightClickCancel(e);
    }

    private void RootSurface_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        CompleteRightClickCancel(e);
    }

    private void BeginRightClickCancel(MouseButtonEventArgs e)
    {
        _rightClickCancelPending = true;
        e.Handled = true;

        if (!RootSurface.IsMouseCaptured)
        {
            RootSurface.CaptureMouse();
        }
    }

    private void CompleteRightClickCancel(MouseButtonEventArgs e)
    {
        if (!_rightClickCancelPending)
        {
            e.Handled = true;
            return;
        }

        _rightClickCancelPending = false;
        e.Handled = true;

        if (RootSurface.IsMouseCaptured)
        {
            RootSurface.ReleaseMouseCapture();
        }

        DialogResult = false;
    }

    private void UpdateSelection(Point start, Point current)
    {
        _selectionRect = new Rect(start, current);
        OverlayLayer.SelectionRect = _selectionRect;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            _showHexColor = !_showHexColor;
            UpdateColorText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C)
        {
            Clipboard.SetText(SelectionColorInspectorService.FormatColor(_currentColor, _showHexColor));
            e.Handled = true;
        }
    }

    private void InitializeTransforms()
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not null)
        {
            _fromDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformFromDevice;
            _toDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformToDevice;
        }
    }

    private static void ForceCrossCursor()
    {
        var cursor = NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IdcCross);
        if (cursor != IntPtr.Zero)
        {
            NativeMethods.SetCursor(cursor);
        }
    }

    private Point GetCursorLocalPoint()
    {
        return NativeMethods.GetCursorPos(out var point)
            ? PointFromScreen(new Point(point.X, point.Y))
            : WpfMouse.GetPosition(RootSurface);
    }

    private static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const int WsExTransparent = 0x00000020;
        public const uint GaRoot = 2;
        public static readonly IntPtr IdcCross = new(32515);

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(DrawingPoint point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("user32.dll")]
        public static extern IntPtr SetCursor(IntPtr hCursor);

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hwnd, index)
                : new IntPtr(GetWindowLong32(hwnd, index));
        }

        public static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hwnd, index, value)
                : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }
}
