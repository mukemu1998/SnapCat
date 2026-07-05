using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfPoint = System.Windows.Point;

namespace SnapCat.App.Windows;

public partial class PinnedImageWindow
{
    private void HideCurrentPinnedWindow()
    {
        Hide();
        _app.PinnedWindowRegistryService.SaveActiveWindows();
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
