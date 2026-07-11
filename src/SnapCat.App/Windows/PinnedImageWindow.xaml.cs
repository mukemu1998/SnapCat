using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using WpfApplication = System.Windows.Application;
using WpfPoint = System.Windows.Point;

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
    private int _rotationDegrees;
    private bool _isHoverOverlayVisible;
    private readonly DispatcherTimer _scaleIndicatorTimer;

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
        _sourceBitmap = PinnedImageBitmapService.LoadImage(imagePath);
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

            if (!_persistedSnapshot.IsVisible)
            {
                return;
            }

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

}
