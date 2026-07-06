using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using SnapCat.App.Services;
using DrawingPoint = System.Drawing.Point;
using FormsScreen = System.Windows.Forms.Screen;
using Point = System.Windows.Point;

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow
{
    private const int AutomationPreviewNodeLimit = 720;
    private const int AutomationPreviewCacheNodeLimitPerWindow = 900;
    private const int AutomationPreviewCacheTotalLimit = 6000;

    private readonly record struct AutomationPreviewCandidate(
        Int32Rect Rect,
        IntPtr WindowHandle,
        int WindowOrder,
        int ElementOrder);

    private void UpdateSmartPreviewThrottled(Point localPoint)
    {
        var now = DateTime.UtcNow;
        var movedDistance = Distance(_lastSmartPreviewPoint, localPoint);
        var movedFarEnough = movedDistance >= 3;
        if (!movedFarEnough && now - _lastSmartPreviewUpdateUtc < SmartPreviewRefreshInterval)
        {
            return;
        }

        _lastSmartPreviewPoint = localPoint;
        _lastSmartPreviewUpdateUtc = now;
        UpdateSmartPreview(localPoint, allowAutomationPreview: movedDistance < 28);
    }

    private void UpdateSmartPreview(Point localPoint, bool allowAutomationPreview)
    {
        var screenPoint = ToScreenPoint(localPoint);
        var region = TryGetImmediateSmartPreviewRegion(screenPoint);
        if (allowAutomationPreview)
        {
            QueueAutomationPreview(screenPoint);
        }

        if (region is null)
        {
            _smartPreviewRegion = null;
            OverlayLayer.ClearSmartPreview();
            return;
        }

        ApplySmartPreviewRegion(region.Value);
    }

    private void ApplySmartPreviewRegion(Int32Rect region)
    {
        if (_smartPreviewRegion is not null && _smartPreviewRegion.Value.Equals(region))
        {
            return;
        }

        _smartPreviewRegion = region;
        var topLeft = _fromDevice.Transform(new Point(
            region.X - _virtualScreenBounds.Left,
            region.Y - _virtualScreenBounds.Top));
        var bottomRight = _fromDevice.Transform(new Point(
            region.X + region.Width - _virtualScreenBounds.Left,
            region.Y + region.Height - _virtualScreenBounds.Top));

        OverlayLayer.SmartPreviewRect = new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    private Int32Rect? TryGetImmediateSmartPreviewRegion(DrawingPoint screenPoint)
    {
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

        var cachedRegion = TryGetCachedAutomationRegion(screenPoint);
        return cachedRegion ?? TryGetWindowRegion(screenPoint, requireNearEdge: false);
    }

    private void StartAutomationPreviewCacheBuild()
    {
        lock (_automationPreviewCacheLock)
        {
            if (_automationPreviewCacheBuilding || _automationPreviewCacheReady)
            {
                return;
            }

            _automationPreviewCacheBuilding = true;
            _automationPreviewCacheCts?.Dispose();
            _automationPreviewCacheCts = new CancellationTokenSource();
        }

        var overlayHwnd = new WindowInteropHelper(this).Handle;
        var shellWindow = NativeMethods.GetShellWindow();
        var token = _automationPreviewCacheCts.Token;
        _ = BuildAutomationPreviewCacheAsync(overlayHwnd, shellWindow, token);
    }

    private void StopAutomationPreviewCacheBuild()
    {
        lock (_automationPreviewCacheLock)
        {
            _automationPreviewCacheCts?.Cancel();
            _automationPreviewCacheCts?.Dispose();
            _automationPreviewCacheCts = null;
            _automationPreviewCache = Array.Empty<AutomationPreviewCandidate>();
            _automationPreviewCacheReady = false;
            _automationPreviewCacheBuilding = false;
        }
    }

    private async Task BuildAutomationPreviewCacheAsync(
        IntPtr overlayHwnd,
        IntPtr shellWindow,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AutomationPreviewCandidate> candidates;

        try
        {
            candidates = await Task.Run(
                () => BuildAutomationPreviewCache(overlayHwnd, shellWindow, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            candidates = Array.Empty<AutomationPreviewCandidate>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            lock (_automationPreviewCacheLock)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                _automationPreviewCache = candidates;
                _automationPreviewCacheReady = candidates.Count > 0;
                _automationPreviewCacheBuilding = false;
            }
        });
    }

    private IReadOnlyList<AutomationPreviewCandidate> BuildAutomationPreviewCache(
        IntPtr overlayHwnd,
        IntPtr shellWindow,
        CancellationToken cancellationToken)
    {
        var candidates = new List<AutomationPreviewCandidate>(1024);
        var seenRects = new HashSet<string>(StringComparer.Ordinal);
        var windowOrder = 0;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested || candidates.Count >= AutomationPreviewCacheTotalLimit)
            {
                return false;
            }

            if (hwnd == overlayHwnd || hwnd == shellWindow || !NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var windowRect)
                || windowRect.Right - windowRect.Left < 12
                || windowRect.Bottom - windowRect.Top < 12)
            {
                return true;
            }

            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                if (root is null)
                {
                    return true;
                }

                var visited = 0;
                var elementOrder = 0;
                CollectAutomationCacheCandidates(
                    root,
                    hwnd,
                    windowOrder,
                    candidates,
                    seenRects,
                    ref visited,
                    ref elementOrder,
                    cancellationToken);
            }
            catch
            {
                // Some windows do not expose UIA trees. The window-level fallback still covers them.
            }

            windowOrder++;
            return true;
        }, IntPtr.Zero);

        return candidates;
    }

    private void CollectAutomationCacheCandidates(
        AutomationElement element,
        IntPtr windowHandle,
        int windowOrder,
        ICollection<AutomationPreviewCandidate> candidates,
        ISet<string> seenRects,
        ref int visited,
        ref int elementOrder,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested
            || visited++ >= AutomationPreviewCacheNodeLimitPerWindow
            || candidates.Count >= AutomationPreviewCacheTotalLimit)
        {
            return;
        }

        try
        {
            var rect = element.Current.BoundingRectangle;
            var normalized = SelectionPreviewRegionService.NormalizeCandidateRect(
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                _virtualScreenBounds);

            if (normalized is not null)
            {
                var key = $"{windowHandle}:{normalized.Value.X}:{normalized.Value.Y}:{normalized.Value.Width}:{normalized.Value.Height}";
                if (seenRects.Add(key))
                {
                    candidates.Add(new AutomationPreviewCandidate(
                        normalized.Value,
                        windowHandle,
                        windowOrder,
                        elementOrder++));
                }
            }
        }
        catch
        {
            // Ignore inaccessible UIA nodes and keep walking siblings.
        }

        AutomationElement? child = null;
        try
        {
            child = TreeWalker.ControlViewWalker.GetFirstChild(element);
        }
        catch
        {
            child = null;
        }

        while (child is not null
            && visited < AutomationPreviewCacheNodeLimitPerWindow
            && candidates.Count < AutomationPreviewCacheTotalLimit
            && !cancellationToken.IsCancellationRequested)
        {
            CollectAutomationCacheCandidates(
                child,
                windowHandle,
                windowOrder,
                candidates,
                seenRects,
                ref visited,
                ref elementOrder,
                cancellationToken);

            try
            {
                child = TreeWalker.ControlViewWalker.GetNextSibling(child);
            }
            catch
            {
                child = null;
            }
        }
    }

    private Int32Rect? TryGetCachedAutomationRegion(DrawingPoint screenPoint)
    {
        if (!_automationPreviewCacheReady)
        {
            return null;
        }

        var hwnd = FindTopLevelWindowUnderPoint(screenPoint);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        IReadOnlyList<AutomationPreviewCandidate> snapshot;
        lock (_automationPreviewCacheLock)
        {
            snapshot = _automationPreviewCache;
        }

        var candidates = snapshot
            .Where(candidate => candidate.WindowHandle == hwnd)
            .Where(candidate => SelectionPreviewRegionService.Contains(candidate.Rect, screenPoint))
            .OrderBy(candidate => candidate.WindowOrder)
            .ThenBy(candidate => candidate.ElementOrder)
            .Select(candidate => candidate.Rect)
            .ToList();

        return SelectionPreviewRegionService.ChooseAutomationCandidate(candidates, screenPoint);
    }

    private void ApplyAutomationSmartPreviewResult(Int32Rect? region, int version)
    {
        if (version != _smartPreviewQueryVersion || region is null)
        {
            return;
        }

        if (_smartPreviewRegion is not null && _smartPreviewRegion.Value.Equals(region.Value))
        {
            return;
        }

        ApplySmartPreviewRegion(region.Value);
    }

    private void QueueAutomationPreview(DrawingPoint screenPoint)
    {
        var version = Interlocked.Increment(ref _smartPreviewQueryVersion);
        _pendingAutomationPreviewPoint = screenPoint;
        if (_smartPreviewQueryInFlight)
        {
            return;
        }

        _smartPreviewQueryInFlight = true;
        _ = RunAutomationPreviewLoopAsync(version, screenPoint);
    }

    private async Task RunAutomationPreviewLoopAsync(int version, DrawingPoint screenPoint)
    {
        var currentVersion = version;
        var currentPoint = screenPoint;

        while (true)
        {
            var region = await Task.Run(() => TryGetAutomationElementRegion(currentPoint));
            await Dispatcher.InvokeAsync(() => ApplyAutomationSmartPreviewResult(region, currentVersion));

            DrawingPoint? nextPoint = null;
            await Dispatcher.InvokeAsync(() =>
            {
                if (_pendingAutomationPreviewPoint is not null && currentVersion != _smartPreviewQueryVersion)
                {
                    nextPoint = _pendingAutomationPreviewPoint.Value;
                    currentVersion = _smartPreviewQueryVersion;
                    _pendingAutomationPreviewPoint = null;
                }
                else
                {
                    _smartPreviewQueryInFlight = false;
                    _pendingAutomationPreviewPoint = null;
                }
            });

            if (nextPoint is null)
            {
                break;
            }

            currentPoint = nextPoint.Value;
        }
    }

    private Int32Rect? TryGetAutomationElementRegion(DrawingPoint screenPoint)
    {
        try
        {
            var hwnd = FindTopLevelWindowUnderPoint(screenPoint);
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return null;
            }

            var candidates = new List<Int32Rect>();
            var visited = 0;
            CollectAutomationCandidates(root, screenPoint, candidates, ref visited);
            return SelectionPreviewRegionService.ChooseAutomationCandidate(candidates, screenPoint);
        }
        catch
        {
            return null;
        }
    }

    private void CollectAutomationCandidates(
        AutomationElement element,
        DrawingPoint screenPoint,
        ICollection<Int32Rect> candidates,
        ref int visited)
    {
        if (visited++ >= AutomationPreviewNodeLimit)
        {
            return;
        }

        Int32Rect? normalized = null;
        var shouldInspectChildren = true;

        try
        {
            var rect = element.Current.BoundingRectangle;
            normalized = SelectionPreviewRegionService.NormalizeCandidateRect(
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                screenPoint,
                _virtualScreenBounds);

            shouldInspectChildren = normalized is not null
                || rect.IsEmpty
                || (screenPoint.X >= rect.Left
                    && screenPoint.X <= rect.Right
                    && screenPoint.Y >= rect.Top
                    && screenPoint.Y <= rect.Bottom);
        }
        catch
        {
            shouldInspectChildren = false;
        }

        if (shouldInspectChildren)
        {
            AutomationElement? child = null;
            try
            {
                child = TreeWalker.ControlViewWalker.GetFirstChild(element);
            }
            catch
            {
                child = null;
            }

            while (child is not null && visited < AutomationPreviewNodeLimit)
            {
                CollectAutomationCandidates(child, screenPoint, candidates, ref visited);

                try
                {
                    child = TreeWalker.ControlViewWalker.GetNextSibling(child);
                }
                catch
                {
                    child = null;
                }
            }
        }

        if (normalized is not null && !candidates.Contains(normalized.Value))
        {
            candidates.Add(normalized.Value);
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
        var hwnd = FindTopLevelWindowUnderPoint(screenPoint);
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out var rect))
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
    }

    private IntPtr FindTopLevelWindowUnderPoint(DrawingPoint screenPoint)
    {
        var overlayHwnd = new WindowInteropHelper(this).Handle;
        var shellWindow = NativeMethods.GetShellWindow();
        var found = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == overlayHwnd || hwnd == shellWindow || !NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            if (screenPoint.X < rect.Left
                || screenPoint.X >= rect.Right
                || screenPoint.Y < rect.Top
                || screenPoint.Y >= rect.Bottom)
            {
                return true;
            }

            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found;
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
