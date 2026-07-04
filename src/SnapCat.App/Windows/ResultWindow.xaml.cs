using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;

namespace SnapCat.App.Windows;

public partial class ResultWindow : Window
{
    private const double WindowCornerRadius = 18;
    private readonly string? _imagePath;

    public ResultWindow(
        string title,
        string status,
        string primaryHeader,
        string primaryText,
        string secondaryHeader,
        string secondaryText,
        string? debugText = null,
        string debugHeader = "OCR 调试信息",
        string? imagePath = null,
        string imageHeader = "截图预览")
    {
        InitializeComponent();
        _imagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath;
        Loaded += ResultWindow_OnLoaded;
        SizeChanged += ResultWindow_OnSizeChanged;

        Title = title;
        TitleTextBlock.Text = title;
        StatusTextBlock.Text = status;
        PrimaryHeaderTextBlock.Text = primaryHeader;
        PrimaryTextBox.Text = primaryText;
        SecondaryHeaderTextBlock.Text = secondaryHeader;
        SecondaryTextBox.Text = secondaryText;

        ConfigureImagePreview(imageHeader);
        ConfigureDebugPanel(debugText, debugHeader);
    }

    private void ConfigureImagePreview(string imageHeader)
    {
        if (string.IsNullOrWhiteSpace(_imagePath))
        {
            return;
        }

        PreviewExpander.Header = imageHeader;
        PreviewExpander.Visibility = Visibility.Visible;
        CopyImagePathButton.Visibility = Visibility.Visible;

        if (!File.Exists(_imagePath))
        {
            PreviewUnavailableTextBlock.Text = $"截图文件不存在：{_imagePath}";
            PreviewUnavailableTextBlock.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(_imagePath);
            bitmap.EndInit();
            bitmap.Freeze();

            PreviewImage.Source = bitmap;
            PreviewScrollViewer.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            PreviewUnavailableTextBlock.Text = $"截图预览加载失败：{ex.Message}";
            PreviewUnavailableTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void ConfigureDebugPanel(string? debugText, string debugHeader)
    {
        if (string.IsNullOrWhiteSpace(debugText))
        {
            return;
        }

        DebugExpander.Header = debugHeader;
        DebugTextBox.Text = debugText;
        DebugExpander.Visibility = Visibility.Visible;
        CopyDebugButton.Visibility = Visibility.Visible;
    }

    private void CopyPrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(PrimaryTextBox.Text ?? string.Empty);
    }

    private void CopySecondaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(SecondaryTextBox.Text ?? string.Empty);
    }

    private void CopyImagePathButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_imagePath ?? string.Empty);
    }

    private void CopyDebugButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(DebugTextBox.Text ?? string.Empty);
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ResultWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowClip();
    }

    private void ResultWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWindowClip();
    }

    private void UpdateWindowClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), WindowCornerRadius, WindowCornerRadius);

        if (WindowBorder.ActualWidth <= 0 || WindowBorder.ActualHeight <= 0)
        {
            return;
        }

        var borderClip = new RectangleGeometry(
            new Rect(0, 0, WindowBorder.ActualWidth, WindowBorder.ActualHeight),
            WindowCornerRadius,
            WindowCornerRadius);

        WindowBorder.Clip = borderClip;

        if (ContentRoot.ActualWidth <= 0 || ContentRoot.ActualHeight <= 0)
        {
            return;
        }

        ContentRoot.Clip = new RectangleGeometry(
            new Rect(0, 0, ContentRoot.ActualWidth, ContentRoot.ActualHeight),
            Math.Max(0, WindowCornerRadius - 4),
            Math.Max(0, WindowCornerRadius - 4));
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
