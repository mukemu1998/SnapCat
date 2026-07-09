using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapCat.App.Services;
using Clipboard = System.Windows.Clipboard;
using WpfPoint = System.Windows.Point;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow
{
    private BitmapSource GetEffectiveBitmapSource()
    {
        return PinnedImageBitmapService.CreateTransformedBitmap(
            _sourceBitmap,
            _flipHorizontally,
            _flipVertically,
            _rotationDegrees);
    }

    private void UpdateImageOrientation(bool resizeWindowToOrientation = false)
    {
        var center = new WpfPoint(Left + (Width / 2d), Top + (Height / 2d));
        var effectiveBitmap = GetEffectiveBitmapSource();

        PinnedImage.Source = effectiveBitmap;
        PinnedImage.RenderTransform = Transform.Identity;

        if (!resizeWindowToOrientation || !IsLoaded)
        {
            return;
        }

        var effectiveSize = GetBitmapSizeInDeviceIndependentPixels(effectiveBitmap);
        _originalWidth = effectiveSize.X;
        _originalHeight = effectiveSize.Y;
        Width = Math.Max(1d, Math.Round(_originalWidth * _currentScale));
        Height = Math.Max(1d, Math.Round(_originalHeight * _currentScale));
        Left = center.X - (Width / 2d);
        Top = center.Y - (Height / 2d);
        _app.PinnedWindowRegistryService.SaveActiveWindows();
    }

    private string CreateOperationImagePath()
    {
        if (!_flipHorizontally && !_flipVertically && _rotationDegrees == 0 && File.Exists(_imagePath))
        {
            return _imagePath;
        }

        return PinnedImageBitmapService.WriteBitmapToTempFile(GetEffectiveBitmapSource(), "pinned-working");
    }

    private void CreateDuplicatePinnedWindow(double offsetX, double offsetY)
    {
        var duplicateImagePath = PinnedImageBitmapService.WriteBitmapToTempFile(GetEffectiveBitmapSource(), "pinned-copy");
        CreatePinnedWindowFromImage(duplicateImagePath, Left + offsetX, Top + offsetY, Width, Height, _currentScale);
    }

    private void CreatePinnedWindowFromImage(string imagePath, double left, double top, double width, double height, double? scale = null)
    {
        var duplicateWindow = new PinnedImageWindow(
            imagePath,
            TranslationLanguageHelper.CloneSettings(_settings));
        duplicateWindow.GroupName = GroupName;

        duplicateWindow.Loaded += (_, _) =>
        {
            if (scale.HasValue)
            {
                duplicateWindow.ImportViewState(left, top, scale.Value);
            }
            else
            {
                duplicateWindow.ImportDisplayedBounds(left, top, width, height);
            }

            duplicateWindow.BringPinnedWindowToFront();
        };

        duplicateWindow.Show();
        duplicateWindow.BringPinnedWindowToFront();
    }

    private void CreateArrayPinnedWindow(PinnedArrayDirection direction, int tileCount)
    {
        var displayedCellBitmap = CreateDisplayedCellBitmap();
        var tiledBitmap = PinnedImageBitmapService.CreateTiledBitmap(displayedCellBitmap, direction, tileCount);
        var imagePath = PinnedImageBitmapService.WriteBitmapToTempFile(tiledBitmap, $"pinned-array-{direction.ToString().ToLowerInvariant()}");
        var layout = PinnedArrayLayoutService.Calculate(direction, Left, Top, Width, Height, tileCount);

        var arrayWindow = new PinnedImageWindow(
            imagePath,
            TranslationLanguageHelper.CloneSettings(_settings));
        arrayWindow.GroupName = GroupName;

        arrayWindow.Loaded += (_, _) =>
        {
            arrayWindow.ImportDisplayedBounds(layout.Left, layout.Top, layout.Width, layout.Height);
            arrayWindow.BringPinnedWindowToFront();
        };

        arrayWindow.Show();
        arrayWindow.BringPinnedWindowToFront();
    }

    private BitmapSource CreateDisplayedCellBitmap()
    {
        PinnedImage.UpdateLayout();

        var cellWidth = Math.Max(1, (int)Math.Round(PinnedImage.ActualWidth > 0 ? PinnedImage.ActualWidth : Width));
        var cellHeight = Math.Max(1, (int)Math.Round(PinnedImage.ActualHeight > 0 ? PinnedImage.ActualHeight : Height));
        var renderBitmap = new RenderTargetBitmap(
            cellWidth,
            cellHeight,
            96,
            96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            var imageBrush = new VisualBrush(PinnedImage)
            {
                Stretch = Stretch.Fill,
                Viewbox = new Rect(0, 0, 1, 1),
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox
            };
            drawingContext.DrawRectangle(imageBrush, null, new Rect(0, 0, cellWidth, cellHeight));
        }

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();
        return renderBitmap;
    }

    private void CopyDisplayedPinnedImageToClipboard()
    {
        Clipboard.SetImage(CreateDisplayedCellBitmap());
    }

    private static WpfPoint GetBitmapSizeInDeviceIndependentPixels(BitmapSource bitmap)
    {
        var dpiX = bitmap.DpiX > 0 ? bitmap.DpiX : 96d;
        var dpiY = bitmap.DpiY > 0 ? bitmap.DpiY : 96d;
        return new WpfPoint(bitmap.PixelWidth * 96d / dpiX, bitmap.PixelHeight * 96d / dpiY);
    }

    private void PasteClipboardImageAsPinnedWindow()
    {
        if (!Clipboard.ContainsImage())
        {
            return;
        }

        var bitmap = Clipboard.GetImage();
        if (bitmap is null)
        {
            return;
        }

        var imagePath = PinnedImageBitmapService.WriteBitmapToTempFile(bitmap, "pinned-paste");
        var pastedWindow = new PinnedImageWindow(
            imagePath,
            TranslationLanguageHelper.CloneSettings(_settings));

        pastedWindow.Loaded += (_, _) =>
        {
            pastedWindow.ImportDisplayedBounds(
                Left + DuplicateOffset,
                Top + DuplicateOffset,
                Math.Max(1d, bitmap.Width),
                Math.Max(1d, bitmap.Height));
            pastedWindow.BringPinnedWindowToFront();
        };

        pastedWindow.Show();
        pastedWindow.BringPinnedWindowToFront();
    }

}
