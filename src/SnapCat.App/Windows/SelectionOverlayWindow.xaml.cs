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

    private void UpdateSmartPreview(Point localPoint)
    {
        var region = TryGetSmartPreviewRegion(localPoint);
        if (region is null)
        {
            _smartPreviewRegion = null;
            SmartPreviewRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        _smartPreviewRegion = region;
        var topLeft = _fromDevice.Transform(new Point(
            region.Value.X - _virtualScreenBounds.Left,
            region.Value.Y - _virtualScreenBounds.Top));
        var bottomRight = _fromDevice.Transform(new Point(
            region.Value.X + region.Value.Width - _virtualScreenBounds.Left,
            region.Value.Y + region.Value.Height - _virtualScreenBounds.Top));

        Canvas.SetLeft(SmartPreviewRectangle, topLeft.X);
        Canvas.SetTop(SmartPreviewRectangle, topLeft.Y);
        SmartPreviewRectangle.Width = Math.Max(0, bottomRight.X - topLeft.X);
        SmartPreviewRectangle.Height = Math.Max(0, bottomRight.Y - topLeft.Y);
        SmartPreviewRectangle.Visibility = Visibility.Visible;
    }

    private Int32Rect? TryGetSmartPreviewRegion(Point localPoint)
    {
        var screenPoint = ToScreenPoint(localPoint);

        var screenEdgeRegion = TryGetScreenEdgeRegion(screenPoint);
        if (screenEdgeRegion is not null)
        {
            return screenEdgeRegion;
        }

        var windowEdgeRegion = TryGetWindowRegion(screenPoint, requireNearEdge: true);
        if (windowEdgeRegion is not null)
        {
            return windowEdgeRegion;
        }

        var automationRegion = TryGetAutomationElementRegion(screenPoint);
        if (automationRegion is not null)
        {
            return automationRegion;
        }

        return TryGetWindowRegion(screenPoint, requireNearEdge: false);
    }

    private Int32Rect? TryGetAutomationElementRegion(DrawingPoint screenPoint)
    {
        try
        {
            return QueryBehindOverlay(() =>
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenPoint.X, screenPoint.Y));
                if (element is null)
                {
                    return null;
                }

                var candidates = new List<Int32Rect>();
                var current = element;
                for (var depth = 0; current is not null && depth < 8; depth++)
                {
                    try
                    {
                        var rect = current.Current.BoundingRectangle;
                        var normalized = NormalizeCandidateRect(rect.Left, rect.Top, rect.Width, rect.Height, screenPoint);
                        if (normalized is not null
                            && !candidates.Any(candidate => candidate.Equals(normalized.Value)))
                        {
                            candidates.Add(normalized.Value);
                        }

                        current = TreeWalker.ControlViewWalker.GetParent(current);
                    }
                    catch
                    {
                        break;
                    }
                }

                return ChooseAutomationCandidate(candidates, screenPoint);
            });
        }
        catch
        {
            return null;
        }
    }

    private Int32Rect? TryGetScreenEdgeRegion(DrawingPoint screenPoint)
    {
        var bounds = Screen.FromPoint(screenPoint).Bounds;
        var nearEdge = Math.Abs(screenPoint.X - bounds.Left) <= ScreenEdgeSnapThreshold
            || Math.Abs(screenPoint.X - (bounds.Right - 1)) <= ScreenEdgeSnapThreshold
            || Math.Abs(screenPoint.Y - bounds.Top) <= ScreenEdgeSnapThreshold
            || Math.Abs(screenPoint.Y - (bounds.Bottom - 1)) <= ScreenEdgeSnapThreshold;

        return nearEdge
            ? ClipToVirtualScreen(new Int32Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
            : null;
    }

    private Int32Rect? TryGetWindowRegion(DrawingPoint screenPoint, bool requireNearEdge)
    {
        return QueryBehindOverlay(() =>
        {
            var hwnd = NativeMethods.WindowFromPoint(screenPoint);
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
            if (root != IntPtr.Zero)
            {
                hwnd = root;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return null;
            }

            var normalized = NormalizeCandidateRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, screenPoint);
            if (normalized is null)
            {
                return null;
            }

            return !requireNearEdge || IsNearRectEdge(normalized.Value, screenPoint, WindowEdgeSnapThreshold)
                ? normalized
                : null;
        });
    }

    private Int32Rect? ChooseAutomationCandidate(IReadOnlyList<Int32Rect> candidates, DrawingPoint screenPoint)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var screenBounds = Screen.FromPoint(screenPoint).Bounds;
        var screenArea = Math.Max(1d, screenBounds.Width * screenBounds.Height);
        var usableCandidates = candidates
            .Where(candidate => candidate.Width * candidate.Height <= screenArea * 0.75d)
            .OrderBy(candidate => candidate.Width * candidate.Height)
            .ToList();

        if (usableCandidates.Count == 0)
        {
            return null;
        }

        var first = candidates[0];
        if (first.Width >= 36 && first.Height >= 18)
        {
            return first;
        }

        return usableCandidates.FirstOrDefault(candidate => candidate.Width >= 36 && candidate.Height >= 18, first);
    }

    private Int32Rect? QueryBehindOverlay(Func<Int32Rect?> query)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return query();
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var transparentStyle = new IntPtr(extendedStyle.ToInt64() | NativeMethods.WsExTransparent);
        try
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, transparentStyle);
            return query();
        }
        finally
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, extendedStyle);
        }
    }

    private Int32Rect? NormalizeCandidateRect(double left, double top, double width, double height, DrawingPoint screenPoint)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(width) || double.IsNaN(height)
            || width < 10 || height < 10)
        {
            return null;
        }

        var rect = new Int32Rect(
            (int)Math.Round(left),
            (int)Math.Round(top),
            (int)Math.Round(width),
            (int)Math.Round(height));

        if (!Contains(rect, screenPoint)
            || rect.Width >= _virtualScreenBounds.Width - 4
            || rect.Height >= _virtualScreenBounds.Height - 4)
        {
            return null;
        }

        return ClipToVirtualScreen(rect);
    }

    private Int32Rect? ClipToVirtualScreen(Int32Rect rect)
    {
        var clippedLeft = Math.Max(rect.X, _virtualScreenBounds.Left);
        var clippedTop = Math.Max(rect.Y, _virtualScreenBounds.Top);
        var clippedRight = Math.Min(rect.X + rect.Width, _virtualScreenBounds.Right);
        var clippedBottom = Math.Min(rect.Y + rect.Height, _virtualScreenBounds.Bottom);
        var clippedWidth = clippedRight - clippedLeft;
        var clippedHeight = clippedBottom - clippedTop;

        return clippedWidth >= 10 && clippedHeight >= 10
            ? new Int32Rect(clippedLeft, clippedTop, clippedWidth, clippedHeight)
            : null;
    }

    private DrawingPoint ToScreenPoint(Point localPoint)
    {
        var devicePoint = _toDevice.Transform(localPoint);
        return new DrawingPoint(
            _virtualScreenBounds.Left + (int)Math.Round(devicePoint.X),
            _virtualScreenBounds.Top + (int)Math.Round(devicePoint.Y));
    }

    private static bool Contains(Int32Rect rect, DrawingPoint point)
    {
        return point.X >= rect.X
            && point.Y >= rect.Y
            && point.X <= rect.X + rect.Width
            && point.Y <= rect.Y + rect.Height;
    }

    private static bool IsNearRectEdge(Int32Rect rect, DrawingPoint point, int threshold)
    {
        return Math.Abs(point.X - rect.X) <= threshold
            || Math.Abs(point.X - (rect.X + rect.Width)) <= threshold
            || Math.Abs(point.Y - rect.Y) <= threshold
            || Math.Abs(point.Y - (rect.Y + rect.Height)) <= threshold;
    }

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
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

    private void CaptureVirtualScreenSnapshot()
    {
        _screenBitmap?.Dispose();
        _screenBitmap = new DrawingBitmap(_virtualScreenBounds.Width, _virtualScreenBounds.Height);
        using var graphics = DrawingGraphics.FromImage(_screenBitmap);
        graphics.CopyFromScreen(
            _virtualScreenBounds.Left,
            _virtualScreenBounds.Top,
            0,
            0,
            new DrawingSize(_virtualScreenBounds.Width, _virtualScreenBounds.Height),
            CopyPixelOperation.SourceCopy);
    }

    private void UpdateColorInspector(Point localPoint)
    {
        if (_screenBitmap is null)
        {
            return;
        }

        var devicePoint = _toDevice.Transform(localPoint);
        var screenX = _virtualScreenBounds.Left + (int)Math.Round(devicePoint.X);
        var screenY = _virtualScreenBounds.Top + (int)Math.Round(devicePoint.Y);
        var bitmapX = Math.Clamp(screenX - _virtualScreenBounds.Left, 0, _screenBitmap.Width - 1);
        var bitmapY = Math.Clamp(screenY - _virtualScreenBounds.Top, 0, _screenBitmap.Height - 1);

        _currentScreenPoint = new DrawingPoint(screenX, screenY);
        _currentColor = _screenBitmap.GetPixel(bitmapX, bitmapY);

        UpdateMagnifierImage(bitmapX, bitmapY);
        UpdateColorText();
        PositionColorInspector(localPoint);
        ColorInspectorPanel.Visibility = Visibility.Visible;
    }

    private void UpdateMagnifierImage(int bitmapX, int bitmapY)
    {
        if (_screenBitmap is null)
        {
            return;
        }

        var sourceLeft = Math.Clamp(bitmapX - (MagnifierSourceSize / 2), 0, Math.Max(0, _screenBitmap.Width - MagnifierSourceSize));
        var sourceTop = Math.Clamp(bitmapY - (MagnifierSourceSize / 2), 0, Math.Max(0, _screenBitmap.Height - MagnifierSourceSize));
        using var source = _screenBitmap.Clone(
            new DrawingRectangle(sourceLeft, sourceTop, MagnifierSourceSize, MagnifierSourceSize),
            _screenBitmap.PixelFormat);

        MagnifierImage.Source = ConvertBitmapToBitmapSource(source);
    }

    private void UpdateColorText()
    {
        CoordinateTextBlock.Text = $"({_currentScreenPoint.X}，{_currentScreenPoint.Y})";
        ColorValueTextBlock.Text = FormatCurrentColor();
        ColorPreviewSwatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
            _currentColor.R,
            _currentColor.G,
            _currentColor.B));
    }

    private string FormatCurrentColor()
    {
        return _showHexColor
            ? $"#{_currentColor.R:X2}{_currentColor.G:X2}{_currentColor.B:X2}"
            : $"{_currentColor.R}, {_currentColor.G}, {_currentColor.B}";
    }

    private void PositionColorInspector(Point localPoint)
    {
        var left = localPoint.X + InspectorOffset;
        var top = localPoint.Y + InspectorOffset;

        if (left + ColorInspectorPanel.Width > ActualWidth - 8)
        {
            left = localPoint.X - ColorInspectorPanel.Width - InspectorOffset;
        }

        if (top + ColorInspectorPanel.Height > ActualHeight - 8)
        {
            top = localPoint.Y - ColorInspectorPanel.Height - InspectorOffset;
        }

        Canvas.SetLeft(ColorInspectorPanel, Math.Max(8, left));
        Canvas.SetTop(ColorInspectorPanel, Math.Max(8, top));
    }

    private static BitmapSource ConvertBitmapToBitmapSource(DrawingBitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(handle);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _screenBitmap?.Dispose();
        base.OnClosed(e);
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
