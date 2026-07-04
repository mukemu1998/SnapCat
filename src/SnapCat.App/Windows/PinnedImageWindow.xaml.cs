using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using WpfApplication = System.Windows.Application;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfPoint = System.Windows.Point;
using WpfMessageBox = System.Windows.MessageBox;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow : Window
{
    private readonly App _app;
    private readonly Int32Rect? _captureRegion;
    private readonly string _imagePath;
    private readonly AppSettings _settings;
    private readonly BitmapImage _bitmap;

    public PinnedImageWindow(string imagePath, AppSettings settings, Int32Rect? captureRegion = null)
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        _captureRegion = captureRegion;
        _imagePath = imagePath;
        _settings = settings;
        _bitmap = LoadImage(imagePath);

        _app.PinnedWindowRegistryService.Register(this);

        PinnedImage.Source = _bitmap;
        Loaded += PinnedImageWindow_OnLoaded;
    }

    private void PinnedImageWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        if (_captureRegion is not null)
        {
            var topLeft = fromDevice.Transform(new WpfPoint(_captureRegion.Value.X, _captureRegion.Value.Y));
            var bottomRight = fromDevice.Transform(new WpfPoint(
                _captureRegion.Value.X + _captureRegion.Value.Width,
                _captureRegion.Value.Y + _captureRegion.Value.Height));

            Left = topLeft.X;
            Top = topLeft.Y;
            Width = bottomRight.X - topLeft.X;
            Height = bottomRight.Y - topLeft.Y;
            return;
        }

        var size = fromDevice.Transform(new WpfPoint(_bitmap.PixelWidth, _bitmap.PixelHeight));
        Width = size.X;
        Height = size.Y;
    }

    private void ContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        CloseOtherPinnedMenuItem.IsEnabled = _app.PinnedWindowRegistryService.HasOtherWindows(this);
    }

    private void CopyImageMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetImage(_bitmap);
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

    private async void OcrMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(CaptureActionKind.OcrOnly, _imagePath, _settings, this);
    }

    private async void OcrTranslateMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(CaptureActionKind.OcrAndTranslate, _imagePath, _settings, this, _captureRegion);
    }

    private async void QrCodeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(CaptureActionKind.QrCode, _imagePath, _settings, this);
    }

    private async void SaveMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(CaptureActionKind.Save, _imagePath, _settings, this);
    }

    private async void SaveAsMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        await _app.CaptureActionService.ExecuteAsync(CaptureActionKind.SaveAs, _imagePath, _settings, this);
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

    private void Border_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        DragMove();
    }

    private void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private static BitmapImage LoadImage(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
