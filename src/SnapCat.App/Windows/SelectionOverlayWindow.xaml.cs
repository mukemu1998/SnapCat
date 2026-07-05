using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Clipboard = System.Windows.Clipboard;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using FormsCursor = System.Windows.Forms.Cursor;
using Point = System.Windows.Point;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow : Window
{
    private const int MagnifierSourceSize = 28;
    private const int InspectorOffset = 18;
    private const double PreviewDragThreshold = 5;
    private const int ScreenEdgeSnapThreshold = 10;
    private const int WindowEdgeSnapThreshold = 10;

    private readonly DrawingRectangle _virtualScreenBounds;
    private Point? _startPoint;
    private Point? _previewClickStartPoint;
    private Int32Rect? _smartPreviewRegion;
    private Int32Rect? _pendingPreviewRegion;
    private Matrix _fromDevice = Matrix.Identity;
    private Matrix _toDevice = Matrix.Identity;
    private DrawingBitmap? _screenBitmap;
    private DrawingColor _currentColor = DrawingColor.Black;
    private DrawingPoint _currentScreenPoint;
    private bool _showHexColor;

    public SelectionOverlayWindow()
    {
        InitializeComponent();

        _virtualScreenBounds = SystemInformation.VirtualScreen;
        Loaded += SelectionOverlayWindow_OnLoaded;
    }

    public Int32Rect? SelectedRegion { get; private set; }

    private void SelectionOverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeTransforms();
        CaptureVirtualScreenSnapshot();

        var topLeftDip = _fromDevice.Transform(new Point(_virtualScreenBounds.Left, _virtualScreenBounds.Top));
        var bottomRightDip = _fromDevice.Transform(new Point(_virtualScreenBounds.Right, _virtualScreenBounds.Bottom));

        Left = topLeftDip.X;
        Top = topLeftDip.Y;
        Width = bottomRightDip.X - topLeftDip.X;
        Height = bottomRightDip.Y - topLeftDip.Y;
        UpdateColorInspector(PointFromScreen(new Point(FormsCursor.Position.X, FormsCursor.Position.Y)));
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

        Canvas.SetLeft(SelectionRectangle, _startPoint.Value.X);
        Canvas.SetTop(SelectionRectangle, _startPoint.Value.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private void RootSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        var current = e.GetPosition(RootSurface);
        UpdateColorInspector(current);

        if (_previewClickStartPoint is not null)
        {
            if (Distance(_previewClickStartPoint.Value, current) <= PreviewDragThreshold)
            {
                return;
            }

            _startPoint = _previewClickStartPoint;
            _previewClickStartPoint = null;
            _pendingPreviewRegion = null;
            SmartPreviewRectangle.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(SelectionRectangle, _startPoint.Value.X);
            Canvas.SetTop(SelectionRectangle, _startPoint.Value.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            SelectionRectangle.Visibility = Visibility.Visible;
        }

        if (_startPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateSmartPreview(current);
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

        var left = Canvas.GetLeft(SelectionRectangle);
        var top = Canvas.GetTop(SelectionRectangle);
        var width = SelectionRectangle.Width;
        var height = SelectionRectangle.Height;

        _startPoint = null;
        _previewClickStartPoint = null;
        _pendingPreviewRegion = null;

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
        DialogResult = false;
    }

    private void UpdateSelection(Point start, Point current)
    {
        var x = Math.Min(start.X, current.X);
        var y = Math.Min(start.Y, current.Y);
        var width = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
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
            Clipboard.SetText(FormatCurrentColor());
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

    private static class NativeMethods
    {
        public const int GwlExStyle = -20;
        public const int WsExTransparent = 0x00000020;
        public const uint GaRoot = 2;

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(DrawingPoint point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

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
}
