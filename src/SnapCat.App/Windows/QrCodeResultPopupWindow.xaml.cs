using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using Clipboard = System.Windows.Clipboard;

namespace SnapCat.App.Windows;

public partial class QrCodeResultPopupWindow : Window
{
    private const double PopupGap = 8;
    private readonly Int32Rect? _captureRegion;
    private readonly Window? _ownerWindow;
    private readonly bool _canCopy;

    public QrCodeResultPopupWindow(
        string status,
        string resultText,
        bool canCopy,
        Int32Rect? captureRegion,
        Window? ownerWindow)
    {
        InitializeComponent();
        _captureRegion = captureRegion;
        _ownerWindow = ownerWindow;
        _canCopy = canCopy && !string.IsNullOrWhiteSpace(resultText);

        StatusTextBlock.Text = status;
        ResultTextBox.Text = resultText;
        CopyButton.IsEnabled = _canCopy;

        Loaded += (_, _) => PositionNearAnchor();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CopyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_canCopy)
        {
            return;
        }

        Clipboard.SetText(ResultTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "二维码内容已复制。";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PositionNearAnchor()
    {
        UpdateLayout();

        var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
        var popupHeight = ActualHeight > 0 ? ActualHeight : Math.Max(MinHeight, Height);
        var anchorRectDip = TryGetAnchorRectDip();
        var workAreaDip = GetWorkAreaDip(anchorRectDip);

        var maxX = Math.Max(workAreaDip.Left, workAreaDip.Right - popupWidth);
        var maxY = Math.Max(workAreaDip.Top, workAreaDip.Bottom - popupHeight);
        var centeredY = Clamp(anchorRectDip.Top + (anchorRectDip.Height - popupHeight) / 2, workAreaDip.Top, maxY);
        var centeredX = Clamp(anchorRectDip.Left + (anchorRectDip.Width - popupWidth) / 2, workAreaDip.Left, maxX);

        var candidates = new[]
        {
            new WpfPoint(anchorRectDip.Right + PopupGap, centeredY),
            new WpfPoint(anchorRectDip.Left - popupWidth - PopupGap, centeredY),
            new WpfPoint(centeredX, anchorRectDip.Bottom + PopupGap),
            new WpfPoint(centeredX, anchorRectDip.Top - popupHeight - PopupGap)
        };

        var best = candidates
            .Select(point => new
            {
                Point = point,
                OffscreenArea = GetOffscreenArea(new WpfRect(point.X, point.Y, popupWidth, popupHeight), workAreaDip),
                Distance = Math.Abs(point.X - anchorRectDip.Right) + Math.Abs(point.Y - anchorRectDip.Top)
            })
            .OrderBy(candidate => candidate.OffscreenArea)
            .ThenBy(candidate => candidate.Distance)
            .First()
            .Point;

        Left = Clamp(best.X, workAreaDip.Left, maxX);
        Top = Clamp(best.Y, workAreaDip.Top, maxY);
    }

    private WpfRect TryGetAnchorRectDip()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        if (_captureRegion is not null)
        {
            var topLeft = fromDevice.Transform(new WpfPoint(_captureRegion.Value.X, _captureRegion.Value.Y));
            var bottomRight = fromDevice.Transform(new WpfPoint(
                _captureRegion.Value.X + _captureRegion.Value.Width,
                _captureRegion.Value.Y + _captureRegion.Value.Height));

            return new WpfRect(topLeft, bottomRight);
        }

        if (_ownerWindow is not null)
        {
            return new WpfRect(_ownerWindow.Left, _ownerWindow.Top, _ownerWindow.ActualWidth, _ownerWindow.ActualHeight);
        }

        return new WpfRect(Left, Top, Width, Height);
    }

    private WpfRect GetWorkAreaDip(WpfRect anchorRectDip)
    {
        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeftPx = toDevice.Transform(new WpfPoint(anchorRectDip.Left, anchorRectDip.Top));
        var bottomRightPx = toDevice.Transform(new WpfPoint(anchorRectDip.Right, anchorRectDip.Bottom));
        var selectionBounds = new Rectangle(
            (int)Math.Round(topLeftPx.X),
            (int)Math.Round(topLeftPx.Y),
            Math.Max(1, (int)Math.Round(bottomRightPx.X - topLeftPx.X)),
            Math.Max(1, (int)Math.Round(bottomRightPx.Y - topLeftPx.Y)));

        var workArea = FormsScreen.FromRectangle(selectionBounds).WorkingArea;
        var workAreaTopLeft = fromDevice.Transform(new WpfPoint(workArea.Left, workArea.Top));
        var workAreaBottomRight = fromDevice.Transform(new WpfPoint(workArea.Right, workArea.Bottom));

        return new WpfRect(workAreaTopLeft, workAreaBottomRight);
    }

    private static double GetOffscreenArea(WpfRect rect, WpfRect bounds)
    {
        var visibleLeft = Math.Max(rect.Left, bounds.Left);
        var visibleTop = Math.Max(rect.Top, bounds.Top);
        var visibleRight = Math.Min(rect.Right, bounds.Right);
        var visibleBottom = Math.Min(rect.Bottom, bounds.Bottom);
        var visibleWidth = Math.Max(0, visibleRight - visibleLeft);
        var visibleHeight = Math.Max(0, visibleBottom - visibleTop);

        return rect.Width * rect.Height - visibleWidth * visibleHeight;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Min(max, Math.Max(min, value));
    }
}
