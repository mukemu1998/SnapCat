using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;

namespace SnapCat.App.Controls;

public sealed class SelectionOverlayRenderLayer : FrameworkElement
{
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

    public void ClearSmartPreview()
    {
        SmartPreviewRect = Rect.Empty;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!SmartPreviewRect.IsEmpty && SelectionRect.IsEmpty)
        {
            DrawRect(
                drawingContext,
                SmartPreviewRect,
                SmartPreviewFill,
                CreatePen(SmartPreviewStroke, 1.5, dashed: true));
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
