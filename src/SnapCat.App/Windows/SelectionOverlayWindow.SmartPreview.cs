using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using DrawingPoint = System.Drawing.Point;
using FormsScreen = System.Windows.Forms.Screen;
using Point = System.Windows.Point;

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow
{
    private void UpdateSmartPreview(Point localPoint)
    {
        var region = TryGetSmartPreviewRegion(localPoint);
        if (region is null)
        {
            _smartPreviewRegion = null;
            SmartPreviewRectangle.Visibility = Visibility.Collapsed;
            return;
        }

        _smartPreviewRegion = region;
        var topLeft = _fromDevice.Transform(new Point(
            region.Value.X - _virtualScreenBounds.Left,
            region.Value.Y - _virtualScreenBounds.Top));
        var bottomRight = _fromDevice.Transform(new Point(
            region.Value.X + region.Value.Width - _virtualScreenBounds.Left,
            region.Value.Y + region.Value.Height - _virtualScreenBounds.Top));

        Canvas.SetLeft(SmartPreviewRectangle, topLeft.X);
        Canvas.SetTop(SmartPreviewRectangle, topLeft.Y);
        SmartPreviewRectangle.Width = Math.Max(0, bottomRight.X - topLeft.X);
        SmartPreviewRectangle.Height = Math.Max(0, bottomRight.Y - topLeft.Y);
        SmartPreviewRectangle.Visibility = Visibility.Visible;
    }

    private Int32Rect? TryGetSmartPreviewRegion(Point localPoint)
    {
        var screenPoint = ToScreenPoint(localPoint);

        var screenEdgeRegion = TryGetScreenEdgeRegion(screenPoint);
        if (screenEdgeRegion is not null)
        {
            return screenEdgeRegion;
        }

        var windowEdgeRegion = TryGetWindowRegion(screenPoint, requireNearEdge: true);
        if (windowEdgeRegion is not null)
        {
            return windowEdgeRegion;
        }

        var automationRegion = TryGetAutomationElementRegion(screenPoint);
        if (automationRegion is not null)
        {
            return automationRegion;
        }

        return TryGetWindowRegion(screenPoint, requireNearEdge: false);
    }

    private Int32Rect? TryGetAutomationElementRegion(DrawingPoint screenPoint)
    {
        try
        {
            return QueryBehindOverlay(() =>
            {
                var element = AutomationElement.FromPoint(new System.Windows.Point(screenPoint.X, screenPoint.Y));
                if (element is null)
                {
                    return null;
                }

                var candidates = new List<Int32Rect>();
                var current = element;
                for (var depth = 0; current is not null && depth < 8; depth++)
                {
                    try
                    {
                        var rect = current.Current.BoundingRectangle;
                        var normalized = NormalizeCandidateRect(rect.Left, rect.Top, rect.Width, rect.Height, screenPoint);
                        if (normalized is not null
                            && !candidates.Any(candidate => candidate.Equals(normalized.Value)))
                        {
                            candidates.Add(normalized.Value);
                        }

                        current = TreeWalker.ControlViewWalker.GetParent(current);
                    }
                    catch
                    {
                        break;
                    }
                }

                return ChooseAutomationCandidate(candidates, screenPoint);
            });
        }
        catch
        {
            return null;
        }
    }

    private Int32Rect? TryGetScreenEdgeRegion(DrawingPoint screenPoint)
    {
        var bounds = FormsScreen.FromPoint(screenPoint).Bounds;
        var nearEdge = Math.Abs(screenPoint.X - bounds.Left) <= ScreenEdgeSnapThreshold
            || Math.Abs(screenPoint.X - (bounds.Right - 1)) <= ScreenEdgeSnapThreshold
            || Math.Abs(screenPoint.Y - bounds.Top) <= ScreenEdgeSnapThreshold
            || Math.Abs(screenPoint.Y - (bounds.Bottom - 1)) <= ScreenEdgeSnapThreshold;

        return nearEdge
            ? ClipToVirtualScreen(new Int32Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height))
            : null;
    }

    private Int32Rect? TryGetWindowRegion(DrawingPoint screenPoint, bool requireNearEdge)
    {
        return QueryBehindOverlay(() =>
        {
            var hwnd = NativeMethods.WindowFromPoint(screenPoint);
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot);
            if (root != IntPtr.Zero)
            {
                hwnd = root;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return null;
            }

            var normalized = NormalizeCandidateRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, screenPoint);
            if (normalized is null)
            {
                return null;
            }

            return !requireNearEdge || IsNearRectEdge(normalized.Value, screenPoint, WindowEdgeSnapThreshold)
                ? normalized
                : null;
        });
    }

    private Int32Rect? ChooseAutomationCandidate(IReadOnlyList<Int32Rect> candidates, DrawingPoint screenPoint)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var screenBounds = FormsScreen.FromPoint(screenPoint).Bounds;
        var screenArea = Math.Max(1d, screenBounds.Width * screenBounds.Height);
        var usableCandidates = candidates
            .Where(candidate => candidate.Width * candidate.Height <= screenArea * 0.75d)
            .OrderBy(candidate => candidate.Width * candidate.Height)
            .ToList();

        if (usableCandidates.Count == 0)
        {
            return null;
        }

        var first = candidates[0];
        if (first.Width >= 36 && first.Height >= 18)
        {
            return first;
        }

        return usableCandidates.FirstOrDefault(candidate => candidate.Width >= 36 && candidate.Height >= 18, first);
    }

    private Int32Rect? QueryBehindOverlay(Func<Int32Rect?> query)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return query();
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        var transparentStyle = new IntPtr(extendedStyle.ToInt64() | NativeMethods.WsExTransparent);
        try
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, transparentStyle);
            return query();
        }
        finally
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, extendedStyle);
        }
    }

    private Int32Rect? NormalizeCandidateRect(double left, double top, double width, double height, DrawingPoint screenPoint)
    {
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(width) || double.IsNaN(height)
            || width < 10 || height < 10)
        {
            return null;
        }

        var rect = new Int32Rect(
            (int)Math.Round(left),
            (int)Math.Round(top),
            (int)Math.Round(width),
            (int)Math.Round(height));

        if (!Contains(rect, screenPoint)
            || rect.Width >= _virtualScreenBounds.Width - 4
            || rect.Height >= _virtualScreenBounds.Height - 4)
        {
            return null;
        }

        return ClipToVirtualScreen(rect);
    }

    private Int32Rect? ClipToVirtualScreen(Int32Rect rect)
    {
        var clippedLeft = Math.Max(rect.X, _virtualScreenBounds.Left);
        var clippedTop = Math.Max(rect.Y, _virtualScreenBounds.Top);
        var clippedRight = Math.Min(rect.X + rect.Width, _virtualScreenBounds.Right);
        var clippedBottom = Math.Min(rect.Y + rect.Height, _virtualScreenBounds.Bottom);
        var clippedWidth = clippedRight - clippedLeft;
        var clippedHeight = clippedBottom - clippedTop;

        return clippedWidth >= 10 && clippedHeight >= 10
            ? new Int32Rect(clippedLeft, clippedTop, clippedWidth, clippedHeight)
            : null;
    }

    private DrawingPoint ToScreenPoint(Point localPoint)
    {
        var devicePoint = _toDevice.Transform(localPoint);
        return new DrawingPoint(
            _virtualScreenBounds.Left + (int)Math.Round(devicePoint.X),
            _virtualScreenBounds.Top + (int)Math.Round(devicePoint.Y));
    }

    private static bool Contains(Int32Rect rect, DrawingPoint point)
    {
        return point.X >= rect.X
            && point.Y >= rect.Y
            && point.X <= rect.X + rect.Width
            && point.Y <= rect.Y + rect.Height;
    }

    private static bool IsNearRectEdge(Int32Rect rect, DrawingPoint point, int threshold)
    {
        return Math.Abs(point.X - rect.X) <= threshold
            || Math.Abs(point.X - (rect.X + rect.Width)) <= threshold
            || Math.Abs(point.Y - rect.Y) <= threshold
            || Math.Abs(point.Y - (rect.Y + rect.Height)) <= threshold;
    }

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
