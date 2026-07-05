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
    private static BitmapSource LoadImage(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private BitmapSource GetEffectiveBitmapSource()
    {
        if (!_flipHorizontally && !_flipVertically)
        {
            return _sourceBitmap;
        }

        var renderBitmap = new RenderTargetBitmap(
            _sourceBitmap.PixelWidth,
            _sourceBitmap.PixelHeight,
            _sourceBitmap.DpiX > 0 ? _sourceBitmap.DpiX : 96,
            _sourceBitmap.DpiY > 0 ? _sourceBitmap.DpiY : 96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            drawingContext.PushTransform(new TranslateTransform(
                _flipHorizontally ? _sourceBitmap.PixelWidth : 0,
                _flipVertically ? _sourceBitmap.PixelHeight : 0));
            drawingContext.PushTransform(new ScaleTransform(
                _flipHorizontally ? -1 : 1,
                _flipVertically ? -1 : 1));
            drawingContext.DrawImage(_sourceBitmap, new Rect(0, 0, _sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight));
            drawingContext.Pop();
            drawingContext.Pop();
        }

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();
        return renderBitmap;
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

        return WriteBitmapToTempFile(GetEffectiveBitmapSource(), "pinned-working");
    }

    private void CreateDuplicatePinnedWindow(double offsetX, double offsetY)
    {
        var duplicateImagePath = WriteBitmapToTempFile(GetEffectiveBitmapSource(), "pinned-copy");
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

    private void CreateArrayPinnedWindow(ArrayDirection direction, int tileCount)
    {
        var displayedCellBitmap = CreateDisplayedCellBitmap();
        var tiledBitmap = CreateTiledBitmap(displayedCellBitmap, direction, tileCount);
        var imagePath = WriteBitmapToTempFile(tiledBitmap, $"pinned-array-{direction.ToString().ToLowerInvariant()}");
        var isHorizontal = direction is ArrayDirection.Left or ArrayDirection.Right;
        var targetWidth = isHorizontal ? Width * tileCount : Width;
        var targetHeight = isHorizontal ? Height : Height * tileCount;
        var targetLeft = Left;
        var targetTop = Top;

        if (direction == ArrayDirection.Right)
        {
            targetLeft += Width;
        }
        else if (direction == ArrayDirection.Left)
        {
            targetLeft -= targetWidth;
        }
        else if (direction == ArrayDirection.Down)
        {
            targetTop += Height;
        }
        else if (direction == ArrayDirection.Up)
        {
            targetTop -= targetHeight;
        }

        var arrayWindow = new PinnedImageWindow(
            imagePath,
            TranslationLanguageHelper.CloneSettings(_settings));
        arrayWindow.GroupName = GroupName;

        arrayWindow.Loaded += (_, _) =>
        {
            arrayWindow.ImportDisplayedBounds(targetLeft, targetTop, targetWidth, targetHeight);
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

        var imagePath = WriteBitmapToTempFile(bitmap, "pinned-paste");
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

    private static int ResolveArrayTileCount(object sender)
    {
        if (sender is FrameworkElement { Tag: string value }
            && int.TryParse(value, out var count)
            && count is >= 1 and <= 99)
        {
            return count;
        }

        return 3;
    }

    private static bool TryResolveArrayDirection(object? value, out ArrayDirection direction)
    {
        if (value is string text
            && Enum.TryParse(text, ignoreCase: true, out direction))
        {
            return true;
        }

        direction = ArrayDirection.Right;
        return false;
    }

    private static BitmapSource CreateTiledBitmap(BitmapSource source, ArrayDirection direction, int tileCount)
    {
        var isHorizontal = direction is ArrayDirection.Left or ArrayDirection.Right;
        var pixelWidth = isHorizontal ? source.PixelWidth * tileCount : source.PixelWidth;
        var pixelHeight = isHorizontal ? source.PixelHeight : source.PixelHeight * tileCount;

        var renderBitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Pbgra32);

        var drawingVisual = new DrawingVisual();
        using (var drawingContext = drawingVisual.RenderOpen())
        {
            for (var index = 0; index < tileCount; index++)
            {
                var x = 0;
                var y = 0;

                if (direction == ArrayDirection.Right)
                {
                    x = source.PixelWidth * index;
                }
                else if (direction == ArrayDirection.Left)
                {
                    x = source.PixelWidth * index;
                }
                else if (direction == ArrayDirection.Down)
                {
                    y = source.PixelHeight * index;
                }
                else if (direction == ArrayDirection.Up)
                {
                    y = source.PixelHeight * index;
                }

                drawingContext.DrawImage(source, new Rect(x, y, source.PixelWidth, source.PixelHeight));
            }
        }

        renderBitmap.Render(drawingVisual);
        renderBitmap.Freeze();
        return renderBitmap;
    }

    private static string WriteBitmapToTempFile(BitmapSource bitmapSource, string prefix)
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "SnapCat");
        Directory.CreateDirectory(tempDirectory);

        var filePath = Path.Combine(tempDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
        return filePath;
    }
}
