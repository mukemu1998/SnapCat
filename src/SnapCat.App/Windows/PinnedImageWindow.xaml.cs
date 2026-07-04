using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using Keyboard = System.Windows.Input.Keyboard;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ModifierKeys = System.Windows.Input.ModifierKeys;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;
using TextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;
using WpfPoint = System.Windows.Point;
using WpfMessageBox = System.Windows.MessageBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow : Window
{
    private const double MinScale = 0.25d;
    private const double MaxScale = 4.0d;
    private const double ScaleStep = 0.10d;
    private const double DuplicateOffset = 24d;
    private readonly App _app;
    private readonly Int32Rect? _captureRegion;
    private readonly string _imagePath;
    private readonly AppSettings _settings;
    private readonly BitmapSource _sourceBitmap;
    private readonly PinnedWindowSnapshot? _persistedSnapshot;
    private double _originalWidth;
    private double _originalHeight;
    private double _currentScale = 1.0d;
    private bool _flipHorizontally;
    private bool _flipVertically;
    private bool _isHoverOverlayVisible;
    private readonly DispatcherTimer _scaleIndicatorTimer;

    private enum ArrayDirection
    {
        Left,
        Right,
        Up,
        Down
    }

    public PinnedImageWindow(
        string imagePath,
        AppSettings settings,
        Int32Rect? captureRegion = null,
        PinnedWindowSnapshot? persistedSnapshot = null)
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        _captureRegion = captureRegion;
        _persistedSnapshot = persistedSnapshot;
        _imagePath = imagePath;
        _settings = settings;
        _sourceBitmap = LoadImage(imagePath);
        PinnedId = string.IsNullOrWhiteSpace(persistedSnapshot?.Id)
            ? Guid.NewGuid().ToString("N")
            : persistedSnapshot.Id;
        GroupName = persistedSnapshot?.GroupName?.Trim() ?? string.Empty;
        _scaleIndicatorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(850)
        };
        _scaleIndicatorTimer.Tick += ScaleIndicatorTimer_OnTick;

        _app.PinnedWindowRegistryService.Register(this);

        PinnedImage.Source = _sourceBitmap;
        UpdateImageOrientation();
        Loaded += PinnedImageWindow_OnLoaded;
    }

    public string PinnedId { get; }

    public string GroupName { get; set; } = string.Empty;

    private void PinnedImageWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        if (_persistedSnapshot is not null)
        {
            var persistedSourceSize = fromDevice.Transform(new WpfPoint(_sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight));
            _originalWidth = persistedSourceSize.X;
            _originalHeight = persistedSourceSize.Y;
            ImportDisplayedBounds(
                _persistedSnapshot.Left,
                _persistedSnapshot.Top,
                _persistedSnapshot.Width,
                _persistedSnapshot.Height);
            Focus();
            _app.PinnedWindowRegistryService.SaveActiveWindows();
            return;
        }

        if (_captureRegion is not null)
        {
            var topLeft = fromDevice.Transform(new WpfPoint(_captureRegion.Value.X, _captureRegion.Value.Y));
            var bottomRight = fromDevice.Transform(new WpfPoint(
                _captureRegion.Value.X + _captureRegion.Value.Width,
                _captureRegion.Value.Y + _captureRegion.Value.Height));

            Left = topLeft.X;
            Top = topLeft.Y;
            _originalWidth = bottomRight.X - topLeft.X;
            _originalHeight = bottomRight.Y - topLeft.Y;
            Width = _originalWidth;
            Height = _originalHeight;
            Focus();
            _app.PinnedWindowRegistryService.SaveActiveWindows();
            return;
        }

        var size = fromDevice.Transform(new WpfPoint(_sourceBitmap.PixelWidth, _sourceBitmap.PixelHeight));
        _originalWidth = size.X;
        _originalHeight = size.Y;
        Width = _originalWidth;
        Height = _originalHeight;
        Focus();
        _app.PinnedWindowRegistryService.SaveActiveWindows();
    }

    private void ContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        SetHoverOverlayVisible(true);
        CloseOtherPinnedMenuItem.IsEnabled = _app.PinnedWindowRegistryService.HasOtherWindows(this);
        ResetOrientationMenuItem.IsEnabled = _flipHorizontally || _flipVertically;
        UngroupedGroupMenuItem.IsChecked = string.IsNullOrWhiteSpace(GroupName);
        GroupOneMenuItem.IsChecked = string.Equals(GroupName, PinnedWindowRegistryService.GroupOneName, StringComparison.Ordinal);
        GroupTwoMenuItem.IsChecked = string.Equals(GroupName, PinnedWindowRegistryService.GroupTwoName, StringComparison.Ordinal);
        GroupThreeMenuItem.IsChecked = string.Equals(GroupName, PinnedWindowRegistryService.GroupThreeName, StringComparison.Ordinal);
    }

    private void RootBorder_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        SetHoverOverlayVisible(true);
    }

    private void RootBorder_OnMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!PinnedContextMenu.IsOpen)
        {
            SetHoverOverlayVisible(false);
        }
    }

    private void CopyImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetImage(GetEffectiveBitmapSource());
    }

    private void DuplicatePinnedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CreateDuplicatePinnedWindow(DuplicateOffset, DuplicateOffset);
    }

    private void CopyImagePathMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_imagePath);
    }

    private void OpenImageLocationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (File.Exists(_imagePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{_imagePath}\"",
                    UseShellExecute = true
                });
                return;
            }

            var directory = Path.GetDirectoryName(_imagePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = true
                });
                return;
            }

            WpfMessageBox.Show(this, "截图文件和目录都不存在。", "打开失败");
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"打开截图位置失败：{ex.Message}", "打开失败");
        }
    }

    private void FlipHorizontalMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _flipHorizontally = !_flipHorizontally;
        UpdateImageOrientation();
    }

    private void FlipVerticalMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _flipVertically = !_flipVertically;
        UpdateImageOrientation();
    }

    private void ResetOrientationMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _flipHorizontally = false;
        _flipVertically = false;
        UpdateImageOrientation();
    }

    private void ArrayRightMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CreateArrayPinnedWindow(ArrayDirection.Right, ResolveArrayTileCount(sender));
    }

    private void ArrayLeftMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CreateArrayPinnedWindow(ArrayDirection.Left, ResolveArrayTileCount(sender));
    }

    private void ArrayDownMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CreateArrayPinnedWindow(ArrayDirection.Down, ResolveArrayTileCount(sender));
    }

    private void ArrayUpMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        CreateArrayPinnedWindow(ArrayDirection.Up, ResolveArrayTileCount(sender));
    }

    private void ArrayCountTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void ArrayCountTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(character => !char.IsDigit(character));
    }

    private void ArrayCountTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not WpfTextBox textBox)
        {
            return;
        }

        if (!TryResolveArrayDirection(textBox.Tag, out var direction))
        {
            e.Handled = true;
            return;
        }

        if (!int.TryParse(textBox.Text, out var tileCount))
        {
            tileCount = 3;
        }

        tileCount = Math.Clamp(tileCount, 1, 99);
        textBox.Text = tileCount.ToString();
        PinnedContextMenu.IsOpen = false;
        CreateArrayPinnedWindow(direction, tileCount);
        e.Handled = true;
    }

    private void UngroupedGroupMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.SetWindowGroup(this, PinnedWindowRegistryService.UngroupedGroupName);
    }

    private void GroupOneMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.SetWindowGroup(this, PinnedWindowRegistryService.GroupOneName);
    }

    private void GroupTwoMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.SetWindowGroup(this, PinnedWindowRegistryService.GroupTwoName);
    }

    private void GroupThreeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.SetWindowGroup(this, PinnedWindowRegistryService.GroupThreeName);
    }

    private void ZoomInMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyScaleDelta(ScaleStep);
    }

    private void ZoomOutMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ApplyScaleDelta(-ScaleStep);
    }

    private void ResetZoomMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        ResetToOriginalScale(GetWindowCenter());
    }

    private async void OcrMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(
            CaptureActionKind.OcrOnly,
            CreateOperationImagePath(),
            _settings,
            this);
    }

    private async void OcrTranslateMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(
            CaptureActionKind.OcrAndTranslate,
            CreateOperationImagePath(),
            _settings,
            this,
            _captureRegion);
    }

    private async void QrCodeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(
            CaptureActionKind.QrCode,
            CreateOperationImagePath(),
            _settings,
            this);
    }

    private async void SaveMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(
            CaptureActionKind.Save,
            CreateOperationImagePath(),
            _settings,
            this);
    }

    private async void SaveAsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(
            CaptureActionKind.SaveAs,
            CreateOperationImagePath(),
            _settings,
            this);
    }

    private void CloseOtherPinnedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.CloseOtherWindows(this);
    }

    private void CloseAllPinnedMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        _app.PinnedWindowRegistryService.CloseAllWindows();
    }

    private void CloseMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HideCurrentMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        HideCurrentPinnedWindow();
    }

    private void HideCurrentButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideCurrentPinnedWindow();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RootBorder_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || IsInteractiveOverlayElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        HideCurrentPinnedWindow();
        e.Handled = true;
    }

    private void Border_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveOverlayElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        Focus();

        if (e.ClickCount >= 2)
        {
            ResetToOriginalScale(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        DragMove();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (IsShortcutMatch(e, _settings.PinnedCloseShortcut))
        {
            Close();
            e.Handled = true;
            return;
        }

        if (IsShortcutMatch(e, _settings.PinnedHideShortcut))
        {
            HideCurrentPinnedWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            Close();
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.C)
            {
                CopyDisplayedPinnedImageToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V)
            {
                PasteClipboardImageAsPinnedWindow();
                e.Handled = true;
                return;
            }

            if (IsZoomInKey(e.Key))
            {
                ApplyScaleDelta(ScaleStep);
                e.Handled = true;
                return;
            }

            if (IsZoomOutKey(e.Key))
            {
                ApplyScaleDelta(-ScaleStep);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                ResetToOriginalScale(GetWindowCenter());
                e.Handled = true;
                return;
            }
        }

        if (TryHandleArrowNudge(e.Key))
        {
            e.Handled = true;
        }
    }

    private void HideCurrentPinnedWindow()
    {
        Hide();
        _app.PinnedWindowRegistryService.SaveActiveWindows();
    }

    private static bool IsShortcutMatch(KeyEventArgs e, string? shortcutText)
    {
        if (string.IsNullOrWhiteSpace(shortcutText))
        {
            return false;
        }

        var parts = shortcutText.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var expectedModifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            expectedModifiers |= parts[index].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "alt" => ModifierKeys.Alt,
                "shift" => ModifierKeys.Shift,
                "win" or "windows" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
        }

        var expectedKey = ResolveShortcutKey(parts[^1]);
        var actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return expectedKey != Key.None
            && expectedKey == actualKey
            && Keyboard.Modifiers == expectedModifiers;
    }

    private static Key ResolveShortcutKey(string keyText)
    {
        if (string.Equals(keyText, "Esc", StringComparison.OrdinalIgnoreCase))
        {
            return Key.Escape;
        }

        if (keyText.Length == 1 && char.IsDigit(keyText[0]))
        {
            return Key.D0 + (keyText[0] - '0');
        }

        return Enum.TryParse<Key>(keyText, true, out var key) ? key : Key.None;
    }

    private void Window_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (PinnedContextMenu.IsOpen)
        {
            return;
        }

        var deltaStep = Math.Sign(e.Delta) * ScaleStep;
        ApplyScaleDelta(deltaStep, e.GetPosition(this));
        e.Handled = true;
    }

    private void ScaleThumb_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        Focus();
    }

    private void ScaleThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var dominantLength = Math.Max(_originalWidth, _originalHeight);
        if (dominantLength <= 0)
        {
            return;
        }

        var delta = (e.HorizontalChange + e.VerticalChange) / dominantLength;
        ApplyScale(_currentScale + delta, new WpfPoint(0, 0));
    }

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

    private void ApplyScaleDelta(double delta, WpfPoint? anchor = null)
    {
        ApplyScale(_currentScale + delta, anchor);
    }

    private void ApplyScale(double requestedScale, WpfPoint? anchorOnWindow = null)
    {
        if (_originalWidth <= 0 || _originalHeight <= 0)
        {
            return;
        }

        var scale = Math.Clamp(requestedScale, MinScale, MaxScale);
        if (Math.Abs(scale - _currentScale) < 0.0001d)
        {
            return;
        }

        var currentWidth = Width <= 0 ? _originalWidth * _currentScale : Width;
        var currentHeight = Height <= 0 ? _originalHeight * _currentScale : Height;
        var anchor = anchorOnWindow ?? new WpfPoint(currentWidth / 2, currentHeight / 2);
        var anchorXRatio = currentWidth <= 0 ? 0.5d : Math.Clamp(anchor.X / currentWidth, 0d, 1d);
        var anchorYRatio = currentHeight <= 0 ? 0.5d : Math.Clamp(anchor.Y / currentHeight, 0d, 1d);
        var anchorScreenX = Left + anchor.X;
        var anchorScreenY = Top + anchor.Y;

        _currentScale = scale;
        var newWidth = Math.Round(_originalWidth * _currentScale);
        var newHeight = Math.Round(_originalHeight * _currentScale);
        Width = newWidth;
        Height = newHeight;
        Left = anchorScreenX - (newWidth * anchorXRatio);
        Top = anchorScreenY - (newHeight * anchorYRatio);
        ShowScaleIndicator();
    }

    private void ResetToOriginalScale(WpfPoint anchor)
    {
        ApplyScale(1.0d, anchor);
    }

    private bool TryHandleArrowNudge(Key key)
    {
        var step = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control ? 10d : 1d;

        switch (key)
        {
            case Key.Left:
                Left -= step;
                return true;
            case Key.Right:
                Left += step;
                return true;
            case Key.Up:
                Top -= step;
                return true;
            case Key.Down:
                Top += step;
                return true;
            default:
                return false;
        }
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

    private void ImportViewState(double left, double top, double scale)
    {
        Left = left;
        Top = top;
        ApplyScale(scale, new WpfPoint(0, 0));
    }

    private void ImportDisplayedBounds(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = Math.Max(1d, width);
        Height = Math.Max(1d, height);

        if (_originalWidth > 0 && _originalHeight > 0)
        {
            var scaleX = Width / _originalWidth;
            var scaleY = Height / _originalHeight;
            _currentScale = Math.Min(scaleX, scaleY);
        }
    }

    public PinnedWindowSnapshot CreateSnapshot()
    {
        var safeWidth = NormalizeSnapshotNumber(Width, _originalWidth);
        var safeHeight = NormalizeSnapshotNumber(Height, _originalHeight);
        return new PinnedWindowSnapshot
        {
            Id = PinnedId,
            ImagePath = _imagePath,
            GroupName = GroupName.Trim(),
            IsVisible = IsVisible,
            Left = NormalizeSnapshotPosition(Left),
            Top = NormalizeSnapshotPosition(Top),
            Width = safeWidth,
            Height = safeHeight,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static double NormalizeSnapshotNumber(double value, double fallback)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return double.IsFinite(fallback) && fallback > 0 ? fallback : 1d;
        }

        return value;
    }

    private static double NormalizeSnapshotPosition(double value)
    {
        return double.IsFinite(value) ? value : 0d;
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

    private static bool IsZoomInKey(Key key)
        => key is Key.OemPlus or Key.Add;

    private static bool IsZoomOutKey(Key key)
        => key is Key.OemMinus or Key.Subtract;

    private static bool IsInteractiveOverlayElement(DependencyObject? source)
    {
        return FindAncestor<WpfButton>(source) is not null
            || FindAncestor<Thumb>(source) is not null;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private WpfPoint GetWindowCenter()
    {
        return new WpfPoint(Width / 2d, Height / 2d);
    }

    private void ScaleIndicatorTimer_OnTick(object? sender, EventArgs e)
    {
        _scaleIndicatorTimer.Stop();
        ScaleIndicator.Visibility = Visibility.Collapsed;
    }

    private void SetHoverOverlayVisible(bool isVisible)
    {
        if (_isHoverOverlayVisible == isVisible)
        {
            return;
        }

        _isHoverOverlayVisible = isVisible;
        HoverOverlay.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        HoverOverlay.Opacity = isVisible ? 1d : 0d;
        HoverOverlay.IsHitTestVisible = isVisible;
    }

    public void BringPinnedWindowToFront()
    {
        Activate();
        Topmost = false;
        Topmost = true;
        Focus();
    }

    private void ShowScaleIndicator()
    {
        ScaleIndicatorText.Text = $"{Math.Round(_currentScale * 100d):0}%";
        ScaleIndicator.Visibility = Visibility.Visible;
        SetHoverOverlayVisible(true);
        _scaleIndicatorTimer.Stop();
        _scaleIndicatorTimer.Start();
    }
}
