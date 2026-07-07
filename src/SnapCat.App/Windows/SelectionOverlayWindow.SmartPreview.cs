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
    private static readonly TimeSpan AutomationPreviewQueryBudget = TimeSpan.FromMilliseconds(80);

    private readonly record struct AutomationPreviewCandidate(
        Int32Rect Rect,
        IntPtr WindowHandle,
        int WindowOrder,
        int ElementOrder);

    private void UpdateSmartPreviewThrottled(Point localPoint)
    {
        var now = DateTime.UtcNow;
        var movedDistance = Distance(_lastSmartPreviewPoint, localPoint);
        var movedFarEnough = movedDistance >= 2;
        if (!movedFarEnough && now - _lastSmartPreviewUpdateUtc < SmartPreviewRefreshInterval)
        {
            return;
        }

        _lastSmartPreviewPoint = localPoint;
        _lastSmartPreviewUpdateUtc = now;
        UpdateSmartPreview(localPoint, allowAutomationPreview: true);
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
            if (!_smartPreviewQueryInFlight)
            {
                _smartPreviewRegion = null;
                OverlayLayer.ClearSmartPreview();
            }

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

        OverlayLayer.ShowSmartPreview(new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(0, bottomRight.X - topLeft.X),
            Math.Max(0, bottomRight.Y - topLeft.Y)));
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
        if (cachedRegion is not null)
        {
            return cachedRegion;
        }

        return TryGetNativeChildWindowRegion(screenPoint)
            ?? TryGetWindowRegion(screenPoint, requireNearEdge: false);
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
            _automationPreviewWindowCache.Clear();
            _automationPreviewWindowCacheBuilding.Clear();
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
        var hwnd = FindTopLevelWindowUnderPoint(screenPoint);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var candidates = new List<Int32Rect>();
        lock (_automationPreviewCacheLock)
        {
            if (_automationPreviewWindowCache.TryGetValue(hwnd, out var windowSnapshot))
            {
                candidates.AddRange(windowSnapshot
                    .Where(candidate => SelectionPreviewRegionService.Contains(candidate.Rect, screenPoint))
                    .OrderBy(candidate => candidate.ElementOrder)
                    .Select(candidate => candidate.Rect));
            }

            if (_automationPreviewCacheReady)
            {
                candidates.AddRange(_automationPreviewCache
                    .Where(candidate => candidate.WindowHandle == hwnd)
                    .Where(candidate => SelectionPreviewRegionService.Contains(candidate.Rect, screenPoint))
                    .OrderBy(candidate => candidate.WindowOrder)
                    .ThenBy(candidate => candidate.ElementOrder)
                    .Select(candidate => candidate.Rect));
            }
        }

        return SelectionPreviewRegionService.ChooseAutomationCandidate(candidates, screenPoint);
    }

    private void ApplyAutomationSmartPreviewResult(Int32Rect? region, int version)
    {
        if (!ShouldApplyAutomationSmartPreviewResult(region, version))
        {
            return;
        }

        if (region is null)
        {
            // A slower UIA lookup can return null even when the immediate/cache path
            // already found a good candidate. Do not let that stale empty result erase
            // a visible preview and create a blink.
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
        StartAutomationPreviewWindowCacheBuild(screenPoint);

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
            var queryTask = Task.Run(() => TryGetAutomationElementRegion(currentPoint));
            ObserveAutomationPreviewResult(queryTask, currentVersion);
            await Task.WhenAny(queryTask, Task.Delay(AutomationPreviewQueryBudget));

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

    private void ObserveAutomationPreviewResult(Task<Int32Rect?> queryTask, int version)
    {
        _ = queryTask.ContinueWith(async task =>
        {
            if (task.Status != TaskStatus.RanToCompletion)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (ShouldApplyAutomationSmartPreviewResult(task.Result, version))
                {
                    ApplyAutomationSmartPreviewResult(task.Result, version);
                }
            });
        }, TaskScheduler.Default);
    }

    private bool ShouldApplyAutomationSmartPreviewResult(Int32Rect? region, int version)
    {
        if (version == _smartPreviewQueryVersion)
        {
            return true;
        }

        // UIA can lag slightly behind the render loop. If the result still
        // covers the current cursor, keep it instead of waiting for the slower
        // full-window cache to catch up.
        if (region is null || !SelectionPreviewRegionService.Contains(region.Value, _currentScreenPoint))
        {
            return false;
        }

        return _smartPreviewRegion is null
            || !SelectionPreviewRegionService.Contains(_smartPreviewRegion.Value, _currentScreenPoint)
            || GetArea(region.Value) <= GetArea(_smartPreviewRegion.Value);
    }

    private static long GetArea(Int32Rect rect)
    {
        return Math.Max(0, rect.Width) * (long)Math.Max(0, rect.Height);
    }

    private void StartAutomationPreviewWindowCacheBuild(DrawingPoint screenPoint)
    {
        var hwnd = FindTopLevelWindowUnderPoint(screenPoint);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        CancellationToken token;
        lock (_automationPreviewCacheLock)
        {
            if (_automationPreviewWindowCache.ContainsKey(hwnd)
                || _automationPreviewWindowCacheBuilding.Contains(hwnd))
            {
                return;
            }

            _automationPreviewWindowCacheBuilding.Add(hwnd);
            token = _automationPreviewCacheCts?.Token ?? CancellationToken.None;
        }

        _ = BuildAutomationPreviewWindowCacheAsync(hwnd, token);
    }

    private async Task BuildAutomationPreviewWindowCacheAsync(IntPtr hwnd, CancellationToken cancellationToken)
    {
        IReadOnlyList<AutomationPreviewCandidate> candidates;
        try
        {
            candidates = await Task.Run(
                () => BuildAutomationPreviewCacheForWindow(hwnd, cancellationToken),
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
                _automationPreviewWindowCache[hwnd] = candidates;
                _automationPreviewWindowCacheBuilding.Remove(hwnd);
            }

            if (candidates.Any(candidate => SelectionPreviewRegionService.Contains(candidate.Rect, _currentScreenPoint)))
            {
                var region = SelectionPreviewRegionService.ChooseAutomationCandidate(
                    candidates.Select(candidate => candidate.Rect).ToList(),
                    _currentScreenPoint);
                if (region is not null)
                {
                    ApplySmartPreviewRegion(region.Value);
                }
            }
        });
    }

    private IReadOnlyList<AutomationPreviewCandidate> BuildAutomationPreviewCacheForWindow(
        IntPtr hwnd,
        CancellationToken cancellationToken)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)
            || !NativeMethods.GetWindowRect(hwnd, out var windowRect)
            || windowRect.Right - windowRect.Left < 12
            || windowRect.Bottom - windowRect.Top < 12)
        {
            return Array.Empty<AutomationPreviewCandidate>();
        }

        var candidates = new List<AutomationPreviewCandidate>(512);
        var seenRects = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            if (root is null)
            {
                return candidates;
            }

            var visited = 0;
            var elementOrder = 0;
            CollectAutomationCacheCandidates(
                root,
                hwnd,
                0,
                candidates,
                seenRects,
                ref visited,
                ref elementOrder,
                cancellationToken);
        }
        catch
        {
            return candidates;
        }

        return candidates;
    }

    private Int32Rect? TryGetAutomationElementRegion(DrawingPoint screenPoint)
    {
        try
        {
            var pointElementRegion = TryGetAutomationPointChainRegion(screenPoint);
            if (pointElementRegion is not null)
            {
                return pointElementRegion;
            }

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

            var candidates = CollectAutomationCandidatesForPoint(root, screenPoint, AutomationPreviewNodeLimit);
            return SelectionPreviewRegionService.ChooseAutomationCandidate(candidates, screenPoint);
        }
        catch
        {
            return null;
        }
    }

    private Int32Rect? TryGetAutomationPointChainRegion(DrawingPoint screenPoint)
    {
        try
        {
            var element = AutomationElement.FromPoint(new Point(screenPoint.X, screenPoint.Y));
            if (element is null)
            {
                return null;
            }

            var candidates = new List<Int32Rect>();
            for (var depth = 0; element is not null && depth < 12; depth++)
            {
                var normalized = TryNormalizeAutomationElementRect(element, screenPoint);
                if (normalized is not null && !candidates.Contains(normalized.Value))
                {
                    candidates.Add(normalized.Value);
                }

                try
                {
                    element = TreeWalker.ControlViewWalker.GetParent(element);
                }
                catch
                {
                    break;
                }
            }

            return SelectionPreviewRegionService.ChooseAutomationCandidate(candidates, screenPoint);
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<Int32Rect> CollectAutomationCandidatesForPoint(
        AutomationElement root,
        DrawingPoint screenPoint,
        int nodeLimit)
    {
        var candidates = new List<Int32Rect>();
        var currentLayer = new List<AutomationElement> { root };
        var visited = 0;

        while (currentLayer.Count > 0 && visited < nodeLimit)
        {
            AutomationElement? bestChild = null;

            foreach (var element in currentLayer)
            {
                if (visited++ >= nodeLimit)
                {
                    break;
                }

                var normalized = TryNormalizeAutomationElementRect(element, screenPoint);
                if (normalized is not null && !candidates.Contains(normalized.Value))
                {
                    candidates.Add(normalized.Value);
                }

                if (bestChild is null)
                {
                    bestChild = FindFirstChildContainingPoint(element, screenPoint, ref visited, nodeLimit);
                }
            }

            if (bestChild is null)
            {
                break;
            }

            currentLayer = [bestChild];
        }

        return candidates;
    }

    private AutomationElement? FindFirstChildContainingPoint(
        AutomationElement element,
        DrawingPoint screenPoint,
        ref int visited,
        int nodeLimit)
    {
        AutomationElement? child;
        try
        {
            child = TreeWalker.ControlViewWalker.GetFirstChild(element);
        }
        catch
        {
            return null;
        }

        while (child is not null && visited < nodeLimit)
        {
            visited++;

            try
            {
                var normalized = TryNormalizeAutomationElementRect(child, screenPoint);
                if (normalized is not null)
                {
                    return child;
                }

                child = TreeWalker.ControlViewWalker.GetNextSibling(child);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private Int32Rect? TryNormalizeAutomationElementRect(
        AutomationElement element,
        DrawingPoint screenPoint)
    {
        try
        {
            var rect = element.Current.BoundingRectangle;
            return SelectionPreviewRegionService.NormalizeCandidateRect(
                rect.Left,
                rect.Top,
                rect.Width,
                rect.Height,
                screenPoint,
                _virtualScreenBounds);
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

    private Int32Rect? TryGetNativeChildWindowRegion(DrawingPoint screenPoint)
    {
        var hwnd = FindTopLevelWindowUnderPoint(screenPoint);
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var best = hwnd;
        var current = hwnd;
        for (var depth = 0; depth < 8; depth++)
        {
            var clientPoint = screenPoint;
            if (!NativeMethods.ScreenToClient(current, ref clientPoint))
            {
                break;
            }

            var child = NativeMethods.ChildWindowFromPointEx(
                current,
                clientPoint,
                NativeMethods.CwpSkipInvisible);
            if (child == IntPtr.Zero || child == current)
            {
                break;
            }

            if (!NativeMethods.GetWindowRect(child, out var childRect)
                || screenPoint.X < childRect.Left
                || screenPoint.X >= childRect.Right
                || screenPoint.Y < childRect.Top
                || screenPoint.Y >= childRect.Bottom)
            {
                break;
            }

            best = child;
            current = child;
        }

        if (!NativeMethods.GetWindowRect(best, out var rect))
        {
            return null;
        }

        return SelectionPreviewRegionService.NormalizeCandidateRect(
            rect.Left,
            rect.Top,
            rect.Right - rect.Left,
            rect.Bottom - rect.Top,
            screenPoint,
            _virtualScreenBounds);
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
