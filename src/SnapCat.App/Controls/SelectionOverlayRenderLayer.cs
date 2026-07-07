using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;

namespace SnapCat.App.Controls;

public sealed class SelectionOverlayRenderLayer : FrameworkElement
{
    private static readonly TimeSpan SmartPreviewTransitionDuration = TimeSpan.FromMilliseconds(105);
    private Rect _smartPreviewDisplayRect = Rect.Empty;
    private Rect _smartPreviewTransitionStartRect = Rect.Empty;
    private Rect _smartPreviewTransitionTargetRect = Rect.Empty;
    private double _smartPreviewOpacity;
    private double _smartPreviewTransitionStartOpacity;
    private double _smartPreviewTransitionTargetOpacity;
    private DateTime _smartPreviewTransitionStartedUtc;
    private bool _isSmartPreviewTransitionActive;

    public static readonly DependencyProperty SmartPreviewRectProperty =
        DependencyProperty.Register(
            nameof(SmartPreviewRect),
            typeof(Rect),
            typeof(SelectionOverlayRenderLayer),
            new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionRectProperty =
        DependencyProperty.Register(
            nameof(SelectionRect),
            typeof(Rect),
            typeof(SelectionOverlayRenderLayer),
            new FrameworkPropertyMetadata(Rect.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SmartPreviewStrokeProperty =
        DependencyProperty.Register(
            nameof(SmartPreviewStroke),
            typeof(MediaBrush),
            typeof(SelectionOverlayRenderLayer),
            new FrameworkPropertyMetadata(MediaBrushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SmartPreviewFillProperty =
        DependencyProperty.Register(
            nameof(SmartPreviewFill),
            typeof(MediaBrush),
            typeof(SelectionOverlayRenderLayer),
            new FrameworkPropertyMetadata(MediaBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionStrokeProperty =
        DependencyProperty.Register(
            nameof(SelectionStroke),
            typeof(MediaBrush),
            typeof(SelectionOverlayRenderLayer),
            new FrameworkPropertyMetadata(MediaBrushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectionFillProperty =
        DependencyProperty.Register(
            nameof(SelectionFill),
            typeof(MediaBrush),
            typeof(SelectionOverlayRenderLayer),
            new FrameworkPropertyMetadata(MediaBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public Rect SmartPreviewRect
    {
        get => (Rect)GetValue(SmartPreviewRectProperty);
        set => SetValue(SmartPreviewRectProperty, value);
    }

    public Rect SelectionRect
    {
        get => (Rect)GetValue(SelectionRectProperty);
        set => SetValue(SelectionRectProperty, value);
    }

    public MediaBrush SmartPreviewStroke
    {
        get => (MediaBrush)GetValue(SmartPreviewStrokeProperty);
        set => SetValue(SmartPreviewStrokeProperty, value);
    }

    public MediaBrush SmartPreviewFill
    {
        get => (MediaBrush)GetValue(SmartPreviewFillProperty);
        set => SetValue(SmartPreviewFillProperty, value);
    }

    public MediaBrush SelectionStroke
    {
        get => (MediaBrush)GetValue(SelectionStrokeProperty);
        set => SetValue(SelectionStrokeProperty, value);
    }

    public MediaBrush SelectionFill
    {
        get => (MediaBrush)GetValue(SelectionFillProperty);
        set => SetValue(SelectionFillProperty, value);
    }

    public void ClearSelection()
    {
        SelectionRect = Rect.Empty;
    }

    public void ShowSmartPreview(Rect rect)
    {
        if (rect.IsEmpty)
        {
            ClearSmartPreview();
            return;
        }

        SmartPreviewRect = rect;

        if (!_smartPreviewDisplayRect.IsEmpty
            && AreClose(_smartPreviewTransitionTargetRect, rect)
            && _smartPreviewTransitionTargetOpacity > 0.99)
        {
            return;
        }

        BeginSmartPreviewTransition(rect, 1);
    }

    public void ClearSmartPreview()
    {
        SmartPreviewRect = Rect.Empty;

        if (_smartPreviewDisplayRect.IsEmpty && _smartPreviewOpacity <= 0)
        {
            return;
        }

        var fadeTarget = _smartPreviewDisplayRect.IsEmpty
            ? _smartPreviewTransitionTargetRect
            : _smartPreviewDisplayRect;
        BeginSmartPreviewTransition(fadeTarget, 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!_smartPreviewDisplayRect.IsEmpty && _smartPreviewOpacity > 0.001 && SelectionRect.IsEmpty)
        {
            drawingContext.PushOpacity(_smartPreviewOpacity);
            DrawRect(
                drawingContext,
                _smartPreviewDisplayRect,
                SmartPreviewFill,
                CreatePen(SmartPreviewStroke, 1.5, dashed: true));
            drawingContext.Pop();
        }

        if (!SelectionRect.IsEmpty)
        {
            DrawRect(
                drawingContext,
                SelectionRect,
                SelectionFill,
                CreatePen(SelectionStroke, 2, dashed: false));
        }
    }

    private void BeginSmartPreviewTransition(Rect targetRect, double targetOpacity)
    {
        UpdateSmartPreviewTransitionFrame(DateTime.UtcNow);

        _smartPreviewTransitionStartRect = _smartPreviewDisplayRect.IsEmpty
            ? targetRect
            : _smartPreviewDisplayRect;
        _smartPreviewTransitionTargetRect = targetRect;
        _smartPreviewTransitionStartOpacity = _smartPreviewOpacity;
        _smartPreviewTransitionTargetOpacity = targetOpacity;
        _smartPreviewTransitionStartedUtc = DateTime.UtcNow;
        _smartPreviewDisplayRect = _smartPreviewTransitionStartRect;

        if (_smartPreviewOpacity <= 0 && targetOpacity > 0)
        {
            _smartPreviewOpacity = 0.18;
        }

        if (!_isSmartPreviewTransitionActive)
        {
            CompositionTarget.Rendering += CompositionTarget_OnRendering;
            _isSmartPreviewTransitionActive = true;
        }

        InvalidateVisual();
    }

    private void CompositionTarget_OnRendering(object? sender, EventArgs e)
    {
        UpdateSmartPreviewTransitionFrame(DateTime.UtcNow);
    }

    private void UpdateSmartPreviewTransitionFrame(DateTime now)
    {
        if (!_isSmartPreviewTransitionActive)
        {
            return;
        }

        var progress = (now - _smartPreviewTransitionStartedUtc).TotalMilliseconds
            / SmartPreviewTransitionDuration.TotalMilliseconds;
        if (progress >= 1)
        {
            _smartPreviewDisplayRect = _smartPreviewTransitionTargetOpacity <= 0
                ? Rect.Empty
                : _smartPreviewTransitionTargetRect;
            _smartPreviewOpacity = _smartPreviewTransitionTargetOpacity;
            CompositionTarget.Rendering -= CompositionTarget_OnRendering;
            _isSmartPreviewTransitionActive = false;
            InvalidateVisual();
            return;
        }

        var eased = EaseOutCubic(Math.Clamp(progress, 0, 1));
        _smartPreviewDisplayRect = InterpolateRect(
            _smartPreviewTransitionStartRect,
            _smartPreviewTransitionTargetRect,
            eased);
        _smartPreviewOpacity = Interpolate(
            _smartPreviewTransitionStartOpacity,
            _smartPreviewTransitionTargetOpacity,
            eased);
        InvalidateVisual();
    }

    private static Rect InterpolateRect(Rect start, Rect end, double progress)
    {
        return new Rect(
            Interpolate(start.X, end.X, progress),
            Interpolate(start.Y, end.Y, progress),
            Math.Max(0, Interpolate(start.Width, end.Width, progress)),
            Math.Max(0, Interpolate(start.Height, end.Height, progress)));
    }

    private static double Interpolate(double start, double end, double progress)
    {
        return start + ((end - start) * progress);
    }

    private static double EaseOutCubic(double progress)
    {
        var inverse = 1 - progress;
        return 1 - (inverse * inverse * inverse);
    }

    private static bool AreClose(Rect first, Rect second)
    {
        return Math.Abs(first.X - second.X) < 0.5
            && Math.Abs(first.Y - second.Y) < 0.5
            && Math.Abs(first.Width - second.Width) < 0.5
            && Math.Abs(first.Height - second.Height) < 0.5;
    }

    private static void DrawRect(DrawingContext drawingContext, Rect rect, MediaBrush fill, MediaPen pen)
    {
        var aligned = AlignToDevicePixels(rect, pen.Thickness);
        drawingContext.DrawRectangle(fill, pen, aligned);
    }

    private static MediaPen CreatePen(MediaBrush brush, double thickness, bool dashed)
    {
        var pen = new MediaPen(brush, thickness);
        if (dashed)
        {
            pen.DashStyle = new DashStyle([2, 2], 0);
        }

        return pen;
    }

    private static Rect AlignToDevicePixels(Rect rect, double strokeThickness)
    {
        var offset = Math.Abs(strokeThickness % 2) > 0.001 ? 0.5 : 0;
        return new Rect(
            Math.Round(rect.X) + offset,
            Math.Round(rect.Y) + offset,
            Math.Max(0, Math.Round(rect.Width)),
            Math.Max(0, Math.Round(rect.Height)));
    }
}
