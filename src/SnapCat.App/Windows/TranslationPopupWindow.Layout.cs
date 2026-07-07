using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class TranslationPopupWindow
{
    private void PositionNearAnchor()
    {
        UpdateLayout();

        var popupWidth = ActualWidth;
        var popupHeight = ActualHeight;
        if (popupWidth <= 0 || popupHeight <= 0)
        {
            popupWidth = Width;
            popupHeight = Math.Max(MinHeight, Height);
        }

        var anchorRectDip = TryGetAnchorRectDip();
        var workAreaDip = GetWorkAreaDip(anchorRectDip);

        var maxX = Math.Max(workAreaDip.Left, workAreaDip.Right - popupWidth);
        var maxY = Math.Max(workAreaDip.Top, workAreaDip.Bottom - popupHeight);

        var centeredX = Clamp(anchorRectDip.Left + (anchorRectDip.Width - popupWidth) / 2, workAreaDip.Left, maxX);
        var centeredY = Clamp(anchorRectDip.Top + (anchorRectDip.Height - popupHeight) / 2, workAreaDip.Top, maxY);
        var rightAlignedX = Clamp(anchorRectDip.Right - popupWidth, workAreaDip.Left, maxX);
        var bottomAlignedY = Clamp(anchorRectDip.Bottom - popupHeight, workAreaDip.Top, maxY);

        var candidates = new[]
        {
            new PopupPositionCandidate(new WpfPoint(anchorRectDip.Right + PopupGap, centeredY), 0),
            new PopupPositionCandidate(new WpfPoint(anchorRectDip.Left - popupWidth - PopupGap, centeredY), 1),
            new PopupPositionCandidate(new WpfPoint(anchorRectDip.Right + PopupGap, bottomAlignedY), 2),
            new PopupPositionCandidate(new WpfPoint(anchorRectDip.Left - popupWidth - PopupGap, bottomAlignedY), 3),
            new PopupPositionCandidate(new WpfPoint(centeredX, anchorRectDip.Bottom + PopupGap), 20),
            new PopupPositionCandidate(new WpfPoint(centeredX, anchorRectDip.Top - popupHeight - PopupGap), 21),
            new PopupPositionCandidate(new WpfPoint(rightAlignedX, anchorRectDip.Bottom + PopupGap), 22),
            new PopupPositionCandidate(new WpfPoint(rightAlignedX, anchorRectDip.Top - popupHeight - PopupGap), 23)
        };

        var bestCandidate = candidates
            .Select(candidate => ScorePopupPosition(candidate, popupWidth, popupHeight, anchorRectDip, workAreaDip))
            .OrderBy(static score => score.OverlapArea)
            .ThenBy(static score => score.OffscreenArea)
            .ThenBy(static score => score.Priority)
            .ThenBy(static score => score.DistanceFromAnchor)
            .First();

        Left = Clamp(bestCandidate.Point.X, workAreaDip.Left, maxX);
        Top = Clamp(bestCandidate.Point.Y, workAreaDip.Top, maxY);
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

    private void AdjustTranslatedTextBoxHeight()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyWindowHeightConstraints();
            UpdateLayout();

            var desiredHeight = Math.Max(MinimumTranslatedTextBoxHeight, TranslatedTextBox.ExtentHeight + TranslatedTextBoxPaddingAllowance);
            var currentPopupHeight = PopupBorder.ActualHeight > 0 ? PopupBorder.ActualHeight : ActualHeight;
            var currentTranslatedHeight = TranslatedTextBox.ActualHeight > 0 ? TranslatedTextBox.ActualHeight : MinimumTranslatedTextBoxHeight;
            var popupChromeHeight = Math.Max(0d, currentPopupHeight - currentTranslatedHeight);
            var maxTranslatedHeight = Math.Max(MinimumTranslatedTextBoxHeight, MaxHeight - popupChromeHeight - PopupGap);
            var finalHeight = Math.Min(desiredHeight, maxTranslatedHeight);

            TranslatedTextBox.Height = finalHeight;
            var needsOverflowScroll = desiredHeight > finalHeight + 1;
            TranslatedTextBox.VerticalScrollBarVisibility = needsOverflowScroll
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;

            InvalidateMeasure();
            UpdateLayout();

            if (_hasAnchoredPosition)
            {
                ConstrainWindowToVisibleBounds();
            }
            else
            {
                PositionWindowIfNeeded();
            }
        }, DispatcherPriority.Background);
    }

    private void ApplyWindowHeightConstraints()
    {
        var anchorRectDip = TryGetAnchorRectDip();
        var workAreaDip = GetWorkAreaDip(anchorRectDip);
        MaxHeight = Math.Max(MinHeight, workAreaDip.Height - PopupVerticalMargin);
    }

    private void PositionWindowIfNeeded()
    {
        if (_hasAnchoredPosition)
        {
            return;
        }

        PositionNearAnchor();
        _hasAnchoredPosition = true;
    }

    private void ConstrainWindowToVisibleBounds()
    {
        UpdateLayout();

        var popupWidth = ActualWidth;
        var popupHeight = ActualHeight;
        if (popupWidth <= 0 || popupHeight <= 0)
        {
            return;
        }

        var workAreaDip = GetWorkAreaDip(TryGetAnchorRectDip());
        var maxX = Math.Max(workAreaDip.Left, workAreaDip.Right - popupWidth);
        var maxY = Math.Max(workAreaDip.Top, workAreaDip.Bottom - popupHeight);

        Left = Clamp(Left, workAreaDip.Left, maxX);
        Top = Clamp(Top, workAreaDip.Top, maxY);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private static PopupPositionScore ScorePopupPosition(
        PopupPositionCandidate candidate,
        double popupWidth,
        double popupHeight,
        WpfRect anchorRect,
        WpfRect workArea)
    {
        var finalPoint = new WpfPoint(
            Clamp(candidate.Point.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - popupWidth)),
            Clamp(candidate.Point.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - popupHeight)));
        var popupRect = new WpfRect(finalPoint.X, finalPoint.Y, popupWidth, popupHeight);
        var overlapArea = CalculateIntersectionArea(popupRect, anchorRect);
        var offscreenArea = popupWidth * popupHeight - CalculateIntersectionArea(popupRect, workArea);
        var distanceFromAnchor = CalculateEdgeDistance(popupRect, anchorRect);

        return new PopupPositionScore(
            finalPoint,
            candidate.Priority,
            overlapArea,
            offscreenArea,
            distanceFromAnchor);
    }

    private static double CalculateIntersectionArea(WpfRect first, WpfRect second)
    {
        var left = Math.Max(first.Left, second.Left);
        var top = Math.Max(first.Top, second.Top);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);

        return right <= left || bottom <= top
            ? 0
            : (right - left) * (bottom - top);
    }

    private static double CalculateEdgeDistance(WpfRect popupRect, WpfRect anchorRect)
    {
        if (popupRect.Bottom <= anchorRect.Top)
        {
            return anchorRect.Top - popupRect.Bottom;
        }

        if (popupRect.Top >= anchorRect.Bottom)
        {
            return popupRect.Top - anchorRect.Bottom;
        }

        if (popupRect.Right <= anchorRect.Left)
        {
            return anchorRect.Left - popupRect.Right;
        }

        if (popupRect.Left >= anchorRect.Right)
        {
            return popupRect.Left - anchorRect.Right;
        }

        return 0;
    }

    private sealed record PopupPositionCandidate(WpfPoint Point, int Priority);

    private sealed record PopupPositionScore(
        WpfPoint Point,
        int Priority,
        double OverlapArea,
        double OffscreenArea,
        double DistanceFromAnchor);
}
