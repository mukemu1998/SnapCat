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

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow
{
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

        Canvas.SetLeft(ColorInspectorPanel, panelPosition.X);
        Canvas.SetTop(ColorInspectorPanel, panelPosition.Y);
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
}
