using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapCat.App.Services;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using Point = System.Windows.Point;
using WpfMouse = System.Windows.Input.Mouse;

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow
{
    private void CaptureOverlaySnapshot()
    {
        if (_screenBitmap is not null && _screenBitmapSource is not null)
        {
            return;
        }

        _screenBitmap?.Dispose();

        if (_hasSnapshotSource)
        {
            PrepareSnapshotImage();
            return;
        }

        _screenBitmap = new DrawingBitmap(_virtualScreenBounds.Width, _virtualScreenBounds.Height);
        using var graphics = DrawingGraphics.FromImage(_screenBitmap);
        graphics.CopyFromScreen(
            _virtualScreenBounds.Left,
            _virtualScreenBounds.Top,
            0,
            0,
            new DrawingSize(_virtualScreenBounds.Width, _virtualScreenBounds.Height),
            CopyPixelOperation.SourceCopy);
        _screenBitmapSource = ConvertBitmapToBitmapSource(_screenBitmap);
    }

    private void PrepareSnapshotImage()
    {
        if (!_hasSnapshotSource || string.IsNullOrWhiteSpace(_snapshotPath))
        {
            return;
        }

        _screenBitmap?.Dispose();
        using var snapshotBitmap = new DrawingBitmap(_snapshotPath);
        _screenBitmap = new DrawingBitmap(snapshotBitmap);
        _screenBitmapSource = ConvertBitmapToBitmapSource(_screenBitmap);
        SnapshotImage.Source = _screenBitmapSource;
        SnapshotImage.Visibility = Visibility.Visible;
    }

    private void UpdateColorInspector(Point localPoint)
    {
        PositionColorInspector(localPoint);
        ColorInspectorPanel.Visibility = Visibility.Visible;

        if (_screenBitmap is null || _screenBitmapSource is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastColorInspectorContentUpdateUtc < ColorInspectorContentRefreshInterval)
        {
            return;
        }

        _lastColorInspectorContentUpdateUtc = now;

        var devicePoint = _toDevice.Transform(localPoint);
        var screenX = _virtualScreenBounds.Left + (int)Math.Round(devicePoint.X);
        var screenY = _virtualScreenBounds.Top + (int)Math.Round(devicePoint.Y);
        var bitmapX = Math.Clamp(screenX - _virtualScreenBounds.Left, 0, _screenBitmap.Width - 1);
        var bitmapY = Math.Clamp(screenY - _virtualScreenBounds.Top, 0, _screenBitmap.Height - 1);

        _currentScreenPoint = new DrawingPoint(screenX, screenY);
        _currentColor = _screenBitmap.GetPixel(bitmapX, bitmapY);

        UpdateMagnifierImage(bitmapX, bitmapY);
        UpdateColorText();
    }

    private void UpdateMagnifierImage(int bitmapX, int bitmapY)
    {
        if (_screenBitmap is null || _screenBitmapSource is null)
        {
            return;
        }

        var sourceLeft = Math.Clamp(bitmapX - (MagnifierSourceSize / 2), 0, Math.Max(0, _screenBitmap.Width - MagnifierSourceSize));
        var sourceTop = Math.Clamp(bitmapY - (MagnifierSourceSize / 2), 0, Math.Max(0, _screenBitmap.Height - MagnifierSourceSize));
        MagnifierImage.Source = new CroppedBitmap(
            _screenBitmapSource,
            new Int32Rect(sourceLeft, sourceTop, MagnifierSourceSize, MagnifierSourceSize));
    }

    private void UpdateColorText()
    {
        CoordinateTextBlock.Text = $"({_currentScreenPoint.X}，{_currentScreenPoint.Y})";
        ColorValueTextBlock.Text = SelectionColorInspectorService.FormatColor(_currentColor, _showHexColor);
        ColorPreviewSwatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(
            _currentColor.R,
            _currentColor.G,
            _currentColor.B));
    }

    private void PositionColorInspector(Point localPoint)
    {
        var panelPosition = SelectionColorInspectorService.CalculatePanelPosition(
            localPoint,
            ColorInspectorPanel.Width,
            ColorInspectorPanel.Height,
            ActualWidth,
            ActualHeight,
            InspectorOffset,
            8);

        ColorInspectorTransform.X = panelPosition.X;
        ColorInspectorTransform.Y = panelPosition.Y;
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
        CompositionTarget.Rendering -= CompositionTarget_OnRendering;
        StopAutomationPreviewCacheBuild();
        WpfMouse.OverrideCursor = null;
        _screenBitmap?.Dispose();
        _screenBitmapSource = null;
        base.OnClosed(e);
    }
}
