using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using SnapCat.App.Services;
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
                        var normalized = SelectionPreviewRegionService.NormalizeCandidateRect(
                            rect.Left,
                            rect.Top,
                            rect.Width,
                            rect.Height,
                            screenPoint,
                            _virtualScreenBounds);
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

                return SelectionPreviewRegionService.ChooseAutomationCandidate(candidates, screenPoint);
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
            ? SelectionPreviewRegionService.ClipToBounds(
                new Int32Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
                _virtualScreenBounds)
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

            var normalized = SelectionPreviewRegionService.NormalizeCandidateRect(
                rect.Left,
                rect.Top,
                rect.Right - rect.Left,
                rect.Bottom - rect.Top,
                screenPoint,
                _virtualScreenBounds);
            if (normalized is null)
            {
                return null;
            }

            return !requireNearEdge || SelectionPreviewRegionService.IsNearRectEdge(normalized.Value, screenPoint, WindowEdgeSnapThreshold)
                ? normalized
                : null;
        });
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

    private DrawingPoint ToScreenPoint(Point localPoint)
    {
        var devicePoint = _toDevice.Transform(localPoint);
        return new DrawingPoint(
            _virtualScreenBounds.Left + (int)Math.Round(devicePoint.X),
            _virtualScreenBounds.Top + (int)Math.Round(devicePoint.Y));
    }

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
