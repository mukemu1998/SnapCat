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
        return PinnedImageBitmapService.CreateFlippedBitmap(_sourceBitmap, _flipHorizontally, _flipVertically);
    }

    private void UpdateImageOrientation()
    {
        PinnedImage.RenderTransformOrigin = new WpfPoint(0.5d, 0.5d);
        PinnedImage.RenderTransform = new ScaleTransform(_flipHorizontally ? -1d : 1d, _flipVertically ? -1d : 1d);
    }

    private string CreateOperationImagePath()
    {
        if (!_flipHorizontally && !_flipVertically && File.Exists(_imagePath))
        {
            return _imagePath;
        }

        return PinnedImageBitmapService.WriteBitmapToTempFile(GetEffectiveBitmapSource(), "pinned-working");
    }

    private void CreateDuplicatePinnedWindow(double offsetX, double offsetY)
    {
        var duplicateImagePath = PinnedImageBitmapService.WriteBitmapToTempFile(GetEffectiveBitmapSource(), "pinned-copy");
        var duplicateWindow = new PinnedImageWindow(
            duplicateImagePath,
            TranslationLanguageHelper.CloneSettings(_settings));

        duplicateWindow.Loaded += (_, _) =>
        {
            duplicateWindow.ImportViewState(Left + offsetX, Top + offsetY, _currentScale);
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
