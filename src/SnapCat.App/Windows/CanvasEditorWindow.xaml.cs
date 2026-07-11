using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnapCat.App.Services;
using Clipboard = System.Windows.Clipboard;
using FormsColorDialog = System.Windows.Forms.ColorDialog;
using FormsScreen = System.Windows.Forms.Screen;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfBinding = System.Windows.Data.Binding;
using WpfImage = System.Windows.Controls.Image;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPath = System.Windows.Shapes.Path;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SnapCat.App.Windows;

public enum CanvasEditorMode
{
    QuickAnnotate,
    FullScreenCanvas
}

public partial class CanvasEditorWindow : Window
{
    private enum EditorTool
    {
        Select,
        Line,
        Arrow,
        Pen,
        Marker,
        Mosaic,
        Eraser,
        Text
    }

    private enum StrokePattern
    {
        Solid,
        Dashed
    }

    private enum SelectionShapeKind
    {
        Rectangle,
        Ellipse
    }

    private enum LineArrowKind
    {
        Line,
        ThinArrow,
        FilledArrow,
        OutlineArrow
    }

    private enum MosaicKind
    {
        Block,
        Soft,
        Heavy
    }

    private enum MosaicApplicationMode
    {
        Rectangle,
        Brush
    }

    private enum ColorSourceKind
    {
        Theme,
        Palette
    }

    private readonly App _app;
    private readonly string _sourceImagePath;
    private readonly BitmapSource _sourceBitmap;
    private readonly CanvasEditorMode _mode;
    private readonly bool _saveAsAndCopyOnConfirm;
    private readonly Stack<CanvasAction> _undoStack = [];
    private readonly Stack<CanvasAction> _redoStack = [];
    private EditorTool _currentTool = EditorTool.Pen;
    private WpfBrush _currentBrush = WpfBrushes.DeepPink;
    private WpfColor _currentColor = System.Windows.Media.Colors.DeepPink;
    private StrokePattern _strokePattern = StrokePattern.Solid;
    private SelectionShapeKind _selectionShapeKind = SelectionShapeKind.Rectangle;
    private LineArrowKind _lineArrowKind = LineArrowKind.Line;
    private MosaicKind _mosaicKind = MosaicKind.Block;
    private MosaicApplicationMode _mosaicApplicationMode = MosaicApplicationMode.Rectangle;
    private ColorSourceKind _colorSourceKind = ColorSourceKind.Theme;
    private string _fontFamilyName = "Microsoft YaHei UI";
    private FontWeight _fontWeight = FontWeights.Normal;
    private WpfPoint _dragStart;
    private WpfPoint _lastEraserPoint;
    private Rect _targetRectInWindow;
    private WpfPoint _toolbarDragStart;
    private Thickness _toolbarDragOriginMargin;
    private FrameworkElement? _previewElement;
    private bool _isDrawingShape;
    private bool _isErasingVisuals;
    private bool _isToolbarDragging;
    private Window? _colorPaletteWindow;
    private Canvas? _activeTextObject;
    private bool _isMovingTextObject;
    private bool _isResizingTextObject;
    private string _textResizeCorner = string.Empty;
    private WpfPoint _textDragStart;
    private WpfPoint _textObjectStart;
    private System.Windows.Size _textObjectStartSize;
    private double _textResizeStartFontSize;

    public CanvasEditorWindow(
        string sourceImagePath,
        Int32Rect? screenRegion = null,
        CanvasEditorMode mode = CanvasEditorMode.QuickAnnotate,
        bool saveAsAndCopyOnConfirm = false)
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        _sourceImagePath = sourceImagePath;
        _mode = mode;
        _saveAsAndCopyOnConfirm = saveAsAndCopyOnConfirm;
        _sourceBitmap = LoadBitmap(sourceImagePath);
        BackgroundImage.Source = _sourceBitmap;
        ApplyColor(GetThemeAccentColor());
        ShapeCanvas.PreviewMouseLeftButtonDown += ShapeCanvas_OnPreviewMouseLeftButtonDown;
        ConfigureBounds(screenRegion);
        ApplyEditorMode();
        ApplyTool(EditorTool.Pen);
        UpdateDrawingAttributes();
    }

    public string? SavedPath { get; private set; }

    private void ApplyEditorMode()
    {
        if (_mode == CanvasEditorMode.FullScreenCanvas)
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(204, 0, 0, 0));
            RootGrid.Background = new SolidColorBrush(WpfColor.FromArgb(102, 0, 0, 0));
            Toolbar.Width = 680;
            ApplyCanvasToolButtonGlyphs();
            ApplyCompactButton(SaveButton, "✓", "保存到默认位置并复制到剪贴板");
            ApplyIconButton(SaveAsButton, CreateFolderIcon(), "另存为");
            ApplyCompactButton(CancelToolButton, "×", "取消");
            SaveAsButton.Visibility = Visibility.Visible;
            UpdateColorSourceUi();
            return;
        }

        Background = WpfBrushes.Transparent;
        RootGrid.Background = WpfBrushes.Transparent;
        Toolbar.Width = 720;
        ApplyCanvasToolButtonGlyphs();
        ApplyCompactButton(
            SaveButton,
            "✓",
            _saveAsAndCopyOnConfirm
                ? "确定编辑并复制到剪贴板"
                : "确定编辑");
        ApplyIconButton(SaveAsButton, CreateFolderIcon(), "另存为");
        ApplyCompactButton(
            CancelToolButton,
            "×",
            _saveAsAndCopyOnConfirm
                ? "取消并返回等待菜单"
                : "取消");
        SaveAsButton.Visibility = _saveAsAndCopyOnConfirm
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateColorSourceUi();
    }

    private void ApplyCanvasToolButtonGlyphs()
    {
        ApplyIconButton(SelectToolButton, CreateSelectionIcon(), "框选");
        ApplyCompactButton(LineToolButton, "⌁", "直线 / 箭头");
        ArrowToolButton.Visibility = Visibility.Collapsed;
        ApplyCompactButton(PenToolButton, "✎", "画笔");
        ApplyIconButton(MarkerToolButton, CreateMarkerIcon(), "马克笔");
        ApplyCompactButton(MosaicToolButton, "▦", "马赛克");
        ApplyIconButton(EraserToolButton, CreateEraserIcon(), "橡皮擦");
        ApplyCompactButton(TextToolButton, "T", "文本输入");
        ApplyCompactButton(UndoToolButton, "↶", "上一步");
        ApplyCompactButton(RedoToolButton, "↷", "下一步");
        ApplyCompactButton(ClearCanvasButton, "↺", "清屏重做");
    }

    private static void ApplyCompactButton(WpfButton button, string glyph, string tooltip)
    {
        button.Width = 42;
        button.Height = 34;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.FontSize = 19;
        button.FontWeight = FontWeights.SemiBold;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Content = new TextBlock
        {
            Text = glyph,
            Width = 24,
            Height = 24,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LineHeight = 24,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight
        };
        button.ToolTip = tooltip;
    }

    private static void ApplyIconButton(WpfButton button, FrameworkElement icon, string tooltip)
    {
        button.Width = 42;
        button.Height = 34;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Content = new Viewbox
        {
            Width = 24,
            Height = 24,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon
        };
        button.ToolTip = tooltip;
    }

    private static Canvas CreateMarkerIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var body = new WpfPath
        {
            Data = Geometry.Parse("M7,17 L15,9 L19,13 L11,21 L6,21 Z"),
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round
        };
        BindIconStroke(body);
        BindIconFill(body);
        canvas.Children.Add(body);

        var cap = new Line
        {
            X1 = 14.5,
            Y1 = 8.5,
            X2 = 19.5,
            Y2 = 13.5,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        BindIconStroke(cap);
        canvas.Children.Add(cap);

        var mark = new Line
        {
            X1 = 5,
            Y1 = 21,
            X2 = 13,
            Y2 = 21,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        BindIconStroke(mark);
        canvas.Children.Add(mark);
        return canvas;
    }

    private static Canvas CreateEraserIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var outline = new WpfRectangle
        {
            Width = 20,
            Height = 12,
            RadiusX = 1.8,
            RadiusY = 1.8,
            StrokeThickness = 2.2,
            Fill = WpfBrushes.Transparent
        };
        BindIconStroke(outline);
        Canvas.SetLeft(outline, 2);
        Canvas.SetTop(outline, 6);
        canvas.Children.Add(outline);

        var divider = new Line
        {
            X1 = 15,
            Y1 = 6,
            X2 = 15,
            Y2 = 18,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square
        };
        BindIconStroke(divider);
        canvas.Children.Add(divider);
        return canvas;
    }

    private static Canvas CreateSelectionIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var rectangle = new WpfRectangle
        {
            Width = 14,
            Height = 11,
            StrokeThickness = 1.8,
            Fill = WpfBrushes.Transparent
        };
        BindIconStroke(rectangle);
        Canvas.SetLeft(rectangle, 3);
        Canvas.SetTop(rectangle, 5);
        canvas.Children.Add(rectangle);

        var ellipse = new Ellipse
        {
            Width = 13,
            Height = 10,
            StrokeThickness = 1.8,
            Fill = WpfBrushes.Transparent
        };
        BindIconStroke(ellipse);
        Canvas.SetLeft(ellipse, 8);
        Canvas.SetTop(ellipse, 9);
        canvas.Children.Add(ellipse);
        return canvas;
    }

    private static Canvas CreateFolderIcon()
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var tab = new WpfPath
        {
            Data = Geometry.Parse("M4,8 L9,8 L11,10 L20,10 L20,18 L4,18 Z"),
            StrokeThickness = 1.9,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = WpfBrushes.Transparent
        };
        BindIconStroke(tab);
        canvas.Children.Add(tab);

        var lid = new Line
        {
            X1 = 4,
            Y1 = 11,
            X2 = 20,
            Y2 = 11,
            StrokeThickness = 1.9,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        BindIconStroke(lid);
        canvas.Children.Add(lid);
        return canvas;
    }

    private static void BindIconStroke(Shape shape)
    {
        shape.SetBinding(Shape.StrokeProperty, CreateButtonForegroundBinding());
    }

    private static void BindIconFill(Shape shape)
    {
        shape.SetBinding(Shape.FillProperty, CreateButtonForegroundBinding());
    }

    private static WpfBinding CreateButtonForegroundBinding()
    {
        return new WpfBinding(nameof(WpfControl.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WpfButton), 1)
        };
    }

    private void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        Topmost = false;
        Topmost = true;
        Activate();
        Focus();
        PositionToolbarNearTarget();
    }

    private void ConfigureBounds(Int32Rect? screenRegion)
    {
        var dpiScale = GetDpiScale();
        if (screenRegion is { Width: > 0, Height: > 0 } region)
        {
            if (_mode == CanvasEditorMode.QuickAnnotate)
            {
                ConfigureQuickAnnotateBounds(region, dpiScale);
                return;
            }

            Left = region.X / dpiScale.X;
            Top = region.Y / dpiScale.Y;
            Width = region.Width / dpiScale.X;
            Height = region.Height / dpiScale.Y;
            EditorSurfaceHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            EditorSurfaceHost.VerticalAlignment = VerticalAlignment.Center;
            EditorSurfaceHost.Margin = new Thickness(0);
            EditorSurface.Width = Width;
            EditorSurface.Height = Height;
            _targetRectInWindow = new Rect(0, 0, Width, Height);
            return;
        }

        var screen = FormsScreen.FromPoint(System.Windows.Forms.Cursor.Position);
        var maxWidth = screen.WorkingArea.Width / dpiScale.X * 0.88d;
        var maxHeight = screen.WorkingArea.Height / dpiScale.Y * 0.82d;
        var sourceWidth = _sourceBitmap.PixelWidth * 96d / Math.Max(1d, _sourceBitmap.DpiX);
        var sourceHeight = _sourceBitmap.PixelHeight * 96d / Math.Max(1d, _sourceBitmap.DpiY);
        var scale = Math.Min(1d, Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight));
        var width = Math.Max(1d, Math.Round(sourceWidth * scale));
        var height = Math.Max(1d, Math.Round(sourceHeight * scale));

        Width = screen.WorkingArea.Width / dpiScale.X;
        Height = screen.WorkingArea.Height / dpiScale.Y;
        Left = screen.WorkingArea.Left / dpiScale.X;
        Top = screen.WorkingArea.Top / dpiScale.Y;
        EditorSurface.Width = width;
        EditorSurface.Height = height;
        _targetRectInWindow = new Rect((Width - width) / 2d, (Height - height) / 2d, width, height);
    }

    private void ConfigureQuickAnnotateBounds(Int32Rect region, WpfPoint dpiScale)
    {
        var screen = FormsScreen.FromRectangle(new System.Drawing.Rectangle(
            region.X,
            region.Y,
            Math.Max(1, region.Width),
            Math.Max(1, region.Height)));
        var bounds = screen.Bounds;
        Left = bounds.Left / dpiScale.X;
        Top = bounds.Top / dpiScale.Y;
        Width = bounds.Width / dpiScale.X;
        Height = bounds.Height / dpiScale.Y;

        var targetLeft = region.X / dpiScale.X - Left;
        var targetTop = region.Y / dpiScale.Y - Top;
        var targetWidth = region.Width / dpiScale.X;
        var targetHeight = region.Height / dpiScale.Y;
        _targetRectInWindow = new Rect(targetLeft, targetTop, targetWidth, targetHeight);

        EditorSurfaceHost.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        EditorSurfaceHost.VerticalAlignment = VerticalAlignment.Top;
        EditorSurfaceHost.Margin = new Thickness(targetLeft, targetTop, 0, 0);
        EditorSurface.Width = targetWidth;
        EditorSurface.Height = targetHeight;
    }

    private void PositionToolbarNearTarget()
    {
        Toolbar.UpdateLayout();
        var toolbarWidth = Math.Max(1d, Toolbar.ActualWidth);
        var toolbarHeight = Math.Max(1d, Toolbar.ActualHeight);
        var gap = 8d;
        WpfPoint position;

        if (_mode == CanvasEditorMode.FullScreenCanvas)
        {
            position = new WpfPoint(
                Math.Max(0d, (ActualWidth - toolbarWidth) / 2d),
                18d);
        }
        else if (_targetRectInWindow.Bottom + gap + toolbarHeight <= ActualHeight)
        {
            position = new WpfPoint(_targetRectInWindow.Left, _targetRectInWindow.Bottom + gap);
        }
        else if (_targetRectInWindow.Top - gap - toolbarHeight >= 0)
        {
            position = new WpfPoint(_targetRectInWindow.Left, _targetRectInWindow.Top - gap - toolbarHeight);
        }
        else if (_targetRectInWindow.Right + gap + toolbarWidth <= ActualWidth)
        {
            position = new WpfPoint(_targetRectInWindow.Right + gap, _targetRectInWindow.Top);
        }
        else if (_targetRectInWindow.Left - gap - toolbarWidth >= 0)
        {
            position = new WpfPoint(_targetRectInWindow.Left - gap - toolbarWidth, _targetRectInWindow.Top);
        }
        else
        {
            position = new WpfPoint(_targetRectInWindow.Left, _targetRectInWindow.Bottom + gap);
        }

        SetToolbarPosition(position, toolbarWidth, toolbarHeight);
    }

    private void SetToolbarPosition(WpfPoint position, double toolbarWidth, double toolbarHeight)
    {
        var left = Math.Clamp(position.X, 6d, Math.Max(6d, ActualWidth - toolbarWidth - 6d));
        var top = Math.Clamp(position.Y, 6d, Math.Max(6d, ActualHeight - toolbarHeight - 6d));
        Toolbar.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        Toolbar.VerticalAlignment = VerticalAlignment.Top;
        Toolbar.Margin = new Thickness(left, top, 0, 0);
    }

    private void ToolButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        if (sender is FrameworkElement { Tag: string tag }
            && Enum.TryParse<EditorTool>(tag, out var tool))
        {
            ApplyTool(tool);
        }
    }

    private void ApplyTool(EditorTool tool)
    {
        _currentTool = tool;
        HideBrushCursor();
        InkLayer.EditingMode = tool switch
        {
            EditorTool.Pen or EditorTool.Marker => InkCanvasEditingMode.Ink,
            EditorTool.Eraser => InkCanvasEditingMode.EraseByPoint,
            _ => InkCanvasEditingMode.None
        };
        InkLayer.IsHitTestVisible = tool != EditorTool.Text;
        InkLayer.Cursor = tool switch
        {
            EditorTool.Text => WpfCursors.IBeam,
            EditorTool.Eraser => WpfCursors.None,
            _ => WpfCursors.Cross
        };

        UpdateDrawingAttributes();
        UpdateToolButtonStates();
        UpdateToolOptionsVisibility();
    }

    private void UpdateDrawingAttributes()
    {
        if (InkLayer is null || ThicknessSlider is null)
        {
            return;
        }

        var thickness = Math.Max(1d, ThicknessSlider.Value);
        var attributes = new DrawingAttributes
        {
            Color = _currentTool == EditorTool.Marker
                ? WpfColor.FromArgb(110, _currentColor.R, _currentColor.G, _currentColor.B)
                : _currentColor,
            Width = _currentTool == EditorTool.Marker ? thickness * 2.6d : thickness,
            Height = _currentTool == EditorTool.Marker ? thickness * 2.6d : thickness,
            FitToCurve = true,
            IgnorePressure = false,
            IsHighlighter = _currentTool == EditorTool.Marker
        };
        InkLayer.DefaultDrawingAttributes = attributes;

        // InkCanvas keeps a separate eraser shape, so the thickness slider must update it too.
        InkLayer.EraserShape = new EllipseStylusShape(thickness, thickness);
    }

    private void InkLayer_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == EditorTool.Eraser)
        {
            HideBrushCursor();
            _lastEraserPoint = e.GetPosition(EditorSurface);
            _isErasingVisuals = true;
            EraseVisualElementsAt(_lastEraserPoint);
            return;
        }

        if (_currentTool is EditorTool.Pen or EditorTool.Marker)
        {
            HideBrushCursor();
            return;
        }

        _dragStart = e.GetPosition(EditorSurface);

        if (_currentTool == EditorTool.Text)
        {
            AddTextBox(_dragStart);
            e.Handled = true;
            return;
        }

        _isDrawingShape = true;
        _previewElement = CreatePreviewElement(_dragStart, _dragStart);
        if (_previewElement is not null)
        {
            ShapeCanvas.Children.Add(_previewElement);
        }

        InkLayer.CaptureMouse();
        e.Handled = true;
    }

    private void ShapeCanvas_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool != EditorTool.Text || e.OriginalSource != ShapeCanvas)
        {
            return;
        }

        AddTextBox(e.GetPosition(EditorSurface));
        e.Handled = true;
    }

    private void InkLayer_OnPreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_currentTool == EditorTool.Eraser
            && _isErasingVisuals
            && e.LeftButton == MouseButtonState.Pressed)
        {
            EraseVisualElementsAlongPath(e.GetPosition(EditorSurface));
            return;
        }

        if (!_isDrawingShape || _previewElement is null)
        {
            UpdateBrushCursor(e);
            return;
        }

        HideBrushCursor();
        UpdatePreviewElement(_previewElement, _dragStart, e.GetPosition(EditorSurface));
        e.Handled = true;
    }

    private void InkLayer_OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        HideBrushCursor();
    }

    private void UpdateBrushCursor(WpfMouseEventArgs e)
    {
        var isBrushTool = _currentTool is EditorTool.Pen or EditorTool.Marker or EditorTool.Eraser
            || (_currentTool == EditorTool.Mosaic && _mosaicApplicationMode == MosaicApplicationMode.Brush);
        if (!isBrushTool
            || e.LeftButton == MouseButtonState.Pressed)
        {
            HideBrushCursor();
            return;
        }

        var point = e.GetPosition(EditorSurface);
        var diameter = _currentTool == EditorTool.Mosaic
            ? Math.Clamp(ThicknessSlider.Value, 8d, 96d)
            : _currentTool == EditorTool.Eraser
                ? Math.Clamp(ThicknessSlider.Value, 2d, 36d)
            : Math.Clamp(Math.Max(4d, InkLayer.DefaultDrawingAttributes.Width), 4d, 48d);
        var gap = diameter / 2d + 2d;
        var outerLength = 7d;
        var halfSize = gap + outerLength;
        var size = halfSize * 2d;

        BrushCursorOverlay.Width = size;
        BrushCursorOverlay.Height = size;
        BrushCursorRing.Width = diameter;
        BrushCursorRing.Height = diameter;
        Canvas.SetLeft(BrushCursorRing, halfSize - diameter / 2d);
        Canvas.SetTop(BrushCursorRing, halfSize - diameter / 2d);

        BrushCursorLeftLine.X1 = 0;
        BrushCursorLeftLine.Y1 = halfSize;
        BrushCursorLeftLine.X2 = halfSize - gap;
        BrushCursorLeftLine.Y2 = halfSize;
        BrushCursorRightLine.X1 = halfSize + gap;
        BrushCursorRightLine.Y1 = halfSize;
        BrushCursorRightLine.X2 = size;
        BrushCursorRightLine.Y2 = halfSize;
        BrushCursorTopLine.X1 = halfSize;
        BrushCursorTopLine.Y1 = 0;
        BrushCursorTopLine.X2 = halfSize;
        BrushCursorTopLine.Y2 = halfSize - gap;
        BrushCursorBottomLine.X1 = halfSize;
        BrushCursorBottomLine.Y1 = halfSize + gap;
        BrushCursorBottomLine.X2 = halfSize;
        BrushCursorBottomLine.Y2 = size;

        Canvas.SetLeft(BrushCursorOverlay, point.X - halfSize);
        Canvas.SetTop(BrushCursorOverlay, point.Y - halfSize);
        BrushCursorOverlay.Visibility = Visibility.Visible;
    }

    private void HideBrushCursor()
    {
        if (BrushCursorOverlay is not null)
        {
            BrushCursorOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void InkLayer_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == EditorTool.Eraser)
        {
            _isErasingVisuals = false;
            return;
        }

        if (!_isDrawingShape || _previewElement is null)
        {
            return;
        }

        _isDrawingShape = false;
        InkLayer.ReleaseMouseCapture();
        UpdatePreviewElement(_previewElement, _dragStart, e.GetPosition(EditorSurface));
        PushUndo(new CanvasAction(_previewElement, null));
        _previewElement = null;
        e.Handled = true;
    }

    private FrameworkElement? CreatePreviewElement(WpfPoint start, WpfPoint end)
    {
        return _currentTool switch
        {
            EditorTool.Select => CreateSelectionShape(start, end),
            EditorTool.Line => CreateLineOrArrow(start, end),
            EditorTool.Arrow => CreateArrow(start, end),
            EditorTool.Mosaic when _mosaicApplicationMode == MosaicApplicationMode.Brush => CreateMosaicBrush(start),
            EditorTool.Mosaic => CreateMosaicImage(start, end),
            _ => null
        };
    }

    private void UpdatePreviewElement(FrameworkElement element, WpfPoint start, WpfPoint end)
    {
        if (element is Line line)
        {
            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
            return;
        }

        if (element is Canvas arrowCanvas && arrowCanvas.Tag as string == "Arrow")
        {
            UpdateArrow(arrowCanvas, start, end);
            return;
        }

        if (element is Canvas { Tag: MosaicBrushStroke mosaicBrush })
        {
            AddMosaicBrushDab(mosaicBrush, end);
            return;
        }

        if (element is WpfImage mosaicImage && mosaicImage.Tag is MosaicImageState mosaicState)
        {
            UpdateMosaicImage(mosaicImage, mosaicState, start, end);
            return;
        }

        if (element is Shape shape)
        {
            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            shape.Width = Math.Max(1d, Math.Abs(end.X - start.X));
            shape.Height = Math.Max(1d, Math.Abs(end.Y - start.Y));
            Canvas.SetLeft(shape, left);
            Canvas.SetTop(shape, top);
        }
    }

    private WpfImage CreateMosaicImage(WpfPoint start, WpfPoint end)
    {
        var mosaicState = new MosaicImageState();
        var image = new WpfImage
        {
            Tag = mosaicState,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        UpdateMosaicImage(image, mosaicState, start, end);
        return image;
    }

    private Canvas CreateMosaicBrush(WpfPoint start)
    {
        var surfaceWidth = Math.Max(1d, EditorSurface.ActualWidth > 0 ? EditorSurface.ActualWidth : EditorSurface.Width);
        var surfaceHeight = Math.Max(1d, EditorSurface.ActualHeight > 0 ? EditorSurface.ActualHeight : EditorSurface.Height);
        var brushStroke = new MosaicBrushStroke();
        var brush = new Canvas
        {
            Width = surfaceWidth,
            Height = surfaceHeight,
            Tag = brushStroke,
            IsHitTestVisible = false
        };
        var image = new WpfImage
        {
            Width = surfaceWidth,
            Height = surfaceHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            Source = CreatePixelatedMosaicSource(0, 0, surfaceWidth, surfaceHeight),
            Clip = brushStroke.Mask
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        brushStroke.Image = image;
        brush.Children.Add(image);
        AddMosaicBrushDab(brushStroke, start, force: true);
        return brush;
    }

    private void AddMosaicBrushDab(MosaicBrushStroke brushStroke, WpfPoint point, bool force = false)
    {
        var diameter = Math.Clamp(ThicknessSlider.Value, 8d, 96d);
        if (!force && brushStroke.HasLastPoint)
        {
            var delta = point - brushStroke.LastPoint;
            var stepSize = Math.Max(2d, diameter * 0.28d);
            var stepCount = Math.Max(1, (int)Math.Ceiling(delta.Length / stepSize));
            for (var step = 1; step <= stepCount; step++)
            {
                var progress = step / (double)stepCount;
                AddMosaicBrushCircle(
                    brushStroke,
                    new WpfPoint(
                        brushStroke.LastPoint.X + delta.X * progress,
                        brushStroke.LastPoint.Y + delta.Y * progress),
                    diameter / 2d);
            }

            brushStroke.LastPoint = point;
            RefreshMosaicBrushClip(brushStroke);
            return;
        }

        AddMosaicBrushCircle(brushStroke, point, diameter / 2d);
        brushStroke.LastPoint = point;
        brushStroke.HasLastPoint = true;
        RefreshMosaicBrushClip(brushStroke);
    }

    private static void AddMosaicBrushCircle(MosaicBrushStroke brushStroke, WpfPoint point, double radius)
    {
        brushStroke.Mask.Children.Add(new EllipseGeometry(point, radius, radius));
    }

    private void UpdateMosaicImage(WpfImage image, MosaicImageState mosaicState, WpfPoint start, WpfPoint end)
    {
        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Max(1d, Math.Abs(end.X - start.X));
        var height = Math.Max(1d, Math.Abs(end.Y - start.Y));

        image.Width = width;
        image.Height = height;
        image.Source = CreatePixelatedMosaicSource(left, top, width, height);
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        mosaicState.Bounds = new Rect(0d, 0d, width, height);
        if (mosaicState.EraseMask.Children.Count == 0)
        {
            image.Clip = null;
        }
    }

    private void EraseVisualElementsAlongPath(WpfPoint currentPoint)
    {
        var radius = GetEraserRadius();
        var delta = currentPoint - _lastEraserPoint;
        var stepSize = Math.Max(5d, radius * 0.9d);
        var stepCount = Math.Max(1, (int)Math.Ceiling(delta.Length / stepSize));
        for (var step = 1; step <= stepCount; step++)
        {
            var progress = step / (double)stepCount;
            EraseVisualElementsAt(new WpfPoint(
                _lastEraserPoint.X + delta.X * progress,
                _lastEraserPoint.Y + delta.Y * progress));
        }

        _lastEraserPoint = currentPoint;
    }

    private void EraseVisualElementsAt(WpfPoint point)
    {
        var radius = GetEraserRadius();
        foreach (var element in ShapeCanvas.Children.OfType<FrameworkElement>().ToList())
        {
            if (element is WpfImage { Tag: MosaicImageState mosaicState } mosaicImage)
            {
                EraseMosaicImageAt(mosaicImage, mosaicState, point, radius);
                continue;
            }

            if (element is Canvas { Tag: MosaicBrushStroke brushStroke })
            {
                EraseMosaicBrushAt(brushStroke, point, radius);
                continue;
            }

            if (IntersectsEraser(element, point, radius))
            {
                ShapeCanvas.Children.Remove(element);
            }
        }
    }

    private double GetEraserRadius()
    {
        return Math.Max(2d, ThicknessSlider.Value / 2d);
    }

    private static bool IntersectsEraser(FrameworkElement element, WpfPoint point, double radius)
    {
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        left = double.IsNaN(left) ? 0d : left;
        top = double.IsNaN(top) ? 0d : top;
        var width = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
        var height = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
        return new Rect(left - radius, top - radius, width + radius * 2d, height + radius * 2d).Contains(point);
    }

    private static void EraseMosaicImageAt(WpfImage mosaicImage, MosaicImageState mosaicState, WpfPoint point, double radius)
    {
        var left = Canvas.GetLeft(mosaicImage);
        var top = Canvas.GetTop(mosaicImage);
        left = double.IsNaN(left) ? 0d : left;
        top = double.IsNaN(top) ? 0d : top;
        var localPoint = new WpfPoint(point.X - left, point.Y - top);
        if (!mosaicState.Bounds.Contains(localPoint))
        {
            return;
        }

        mosaicState.EraseMask.Children.Add(new EllipseGeometry(localPoint, radius, radius));
        mosaicImage.Clip = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(mosaicState.Bounds),
            mosaicState.EraseMask);
    }

    private static void EraseMosaicBrushAt(MosaicBrushStroke brushStroke, WpfPoint point, double radius)
    {
        if (!brushStroke.Mask.FillContains(point))
        {
            return;
        }

        brushStroke.EraseMask.Children.Add(new EllipseGeometry(point, radius, radius));
        RefreshMosaicBrushClip(brushStroke);
    }

    private static void RefreshMosaicBrushClip(MosaicBrushStroke brushStroke)
    {
        if (brushStroke.Image is null)
        {
            return;
        }

        brushStroke.Image.Clip = brushStroke.EraseMask.Children.Count == 0
            ? brushStroke.Mask
            : new CombinedGeometry(GeometryCombineMode.Exclude, brushStroke.Mask, brushStroke.EraseMask);
    }

    private BitmapSource CreatePixelatedMosaicSource(double left, double top, double width, double height)
    {
        var sourceRect = ToSourcePixelRect(left, top, width, height);
        var crop = new CroppedBitmap(_sourceBitmap, sourceRect);
        BitmapSource source = crop;
        if (source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Pbgra32)
        {
            source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        }

        var stride = sourceRect.Width * 4;
        var pixels = new byte[stride * sourceRect.Height];
        source.CopyPixels(pixels, stride, 0);

        var blockSize = GetMosaicBlockSize(_mosaicKind);
        for (var y = 0; y < sourceRect.Height; y += blockSize)
        {
            var blockHeight = Math.Min(blockSize, sourceRect.Height - y);
            for (var x = 0; x < sourceRect.Width; x += blockSize)
            {
                var blockWidth = Math.Min(blockSize, sourceRect.Width - x);
                FillMosaicBlock(pixels, stride, x, y, blockWidth, blockHeight);
            }
        }

        var bitmap = BitmapSource.Create(
            sourceRect.Width,
            sourceRect.Height,
            _sourceBitmap.DpiX,
            _sourceBitmap.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }

    private Int32Rect ToSourcePixelRect(double left, double top, double width, double height)
    {
        var surfaceWidth = Math.Max(1d, EditorSurface.ActualWidth > 0 ? EditorSurface.ActualWidth : EditorSurface.Width);
        var surfaceHeight = Math.Max(1d, EditorSurface.ActualHeight > 0 ? EditorSurface.ActualHeight : EditorSurface.Height);
        var pixelLeft = (int)Math.Floor(left / surfaceWidth * _sourceBitmap.PixelWidth);
        var pixelTop = (int)Math.Floor(top / surfaceHeight * _sourceBitmap.PixelHeight);
        var pixelRight = (int)Math.Ceiling((left + width) / surfaceWidth * _sourceBitmap.PixelWidth);
        var pixelBottom = (int)Math.Ceiling((top + height) / surfaceHeight * _sourceBitmap.PixelHeight);

        pixelLeft = Math.Clamp(pixelLeft, 0, Math.Max(0, _sourceBitmap.PixelWidth - 1));
        pixelTop = Math.Clamp(pixelTop, 0, Math.Max(0, _sourceBitmap.PixelHeight - 1));
        pixelRight = Math.Clamp(pixelRight, pixelLeft + 1, _sourceBitmap.PixelWidth);
        pixelBottom = Math.Clamp(pixelBottom, pixelTop + 1, _sourceBitmap.PixelHeight);
        return new Int32Rect(pixelLeft, pixelTop, pixelRight - pixelLeft, pixelBottom - pixelTop);
    }

    private static int GetMosaicBlockSize(MosaicKind kind)
    {
        return kind switch
        {
            MosaicKind.Soft => 10,
            MosaicKind.Heavy => 24,
            _ => 16
        };
    }

    private static void FillMosaicBlock(byte[] pixels, int stride, int x, int y, int width, int height)
    {
        long blue = 0;
        long green = 0;
        long red = 0;
        long alpha = 0;
        var count = 0;

        for (var row = 0; row < height; row++)
        {
            var offset = ((y + row) * stride) + (x * 4);
            for (var column = 0; column < width; column++)
            {
                blue += pixels[offset];
                green += pixels[offset + 1];
                red += pixels[offset + 2];
                alpha += pixels[offset + 3];
                offset += 4;
                count++;
            }
        }

        if (count <= 0)
        {
            return;
        }

        var averageBlue = (byte)(blue / count);
        var averageGreen = (byte)(green / count);
        var averageRed = (byte)(red / count);
        var averageAlpha = (byte)(alpha / count);
        for (var row = 0; row < height; row++)
        {
            var offset = ((y + row) * stride) + (x * 4);
            for (var column = 0; column < width; column++)
            {
                pixels[offset] = averageBlue;
                pixels[offset + 1] = averageGreen;
                pixels[offset + 2] = averageRed;
                pixels[offset + 3] = averageAlpha;
                offset += 4;
            }
        }
    }

    private Shape CreateSelectionShape(WpfPoint start, WpfPoint end)
    {
        return _selectionShapeKind == SelectionShapeKind.Ellipse
            ? CreateEllipse(start, end, null, _currentBrush, Math.Max(1d, ThicknessSlider.Value), GetDashArray())
            : CreateRectangle(start, end, null, _currentBrush, Math.Max(1d, ThicknessSlider.Value), GetDashArray());
    }

    private Line CreateLine(WpfPoint start, WpfPoint end)
    {
        return new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = _currentBrush,
            StrokeThickness = Math.Max(1d, ThicknessSlider.Value),
            StrokeDashArray = GetDashArray(),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
    }

    private FrameworkElement CreateLineOrArrow(WpfPoint start, WpfPoint end)
    {
        return _lineArrowKind == LineArrowKind.Line
            ? CreateLine(start, end)
            : CreateArrow(start, end);
    }

    private Canvas CreateArrow(WpfPoint start, WpfPoint end)
    {
        var canvas = new Canvas { Tag = "Arrow" };
        canvas.Children.Add(CreateLine(start, end));
        canvas.Children.Add(new Polygon { Fill = _currentBrush, Stroke = _currentBrush });
        canvas.Children.Add(new Polyline { Stroke = _currentBrush });
        UpdateArrow(canvas, start, end);
        return canvas;
    }

    private void UpdateArrow(Canvas canvas, WpfPoint start, WpfPoint end)
    {
        var thickness = Math.Max(1d, ThicknessSlider.Value);
        var isFilled = _lineArrowKind == LineArrowKind.FilledArrow;
        var isOutline = _lineArrowKind == LineArrowKind.OutlineArrow;
        var lineThickness = isFilled ? Math.Max(10d, thickness * 2.2d) : Math.Max(2d, thickness);
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance < 0.1d)
        {
            distance = 0.1d;
        }

        var unitX = deltaX / distance;
        var unitY = deltaY / distance;
        var normalX = -unitY;
        var normalY = unitX;
        var headLength = _lineArrowKind switch
        {
            LineArrowKind.FilledArrow => Math.Max(34d, lineThickness * 4.4d),
            LineArrowKind.OutlineArrow => Math.Max(22d, thickness * 5.2d),
            _ => Math.Max(16d, thickness * 4.2d)
        };
        var headHalfWidth = _lineArrowKind switch
        {
            LineArrowKind.FilledArrow => Math.Max(18d, lineThickness * 1.7d),
            LineArrowKind.OutlineArrow => Math.Max(9d, thickness * 2.2d),
            _ => Math.Max(6d, thickness * 1.7d)
        };
        var shaftEndDistance = Math.Max(0d, distance - headLength);
        var shaftEnd = new WpfPoint(
            start.X + (unitX * shaftEndDistance),
            start.Y + (unitY * shaftEndDistance));

        if (canvas.Children[0] is Line line)
        {
            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = shaftEnd.X;
            line.Y2 = shaftEnd.Y;
            line.Stroke = _currentBrush;
            line.StrokeThickness = lineThickness;
            line.StrokeDashArray = GetDashArray();
            line.StrokeStartLineCap = PenLineCap.Round;
            line.StrokeEndLineCap = isFilled ? PenLineCap.Flat : PenLineCap.Round;
        }

        if (canvas.Children[1] is not Polygon filledHead || canvas.Children[2] is not Polyline outlineHead)
        {
            return;
        }

        var baseCenter = new WpfPoint(
            end.X - (unitX * headLength),
            end.Y - (unitY * headLength));
        var headLeft = new WpfPoint(
            baseCenter.X + (normalX * headHalfWidth),
            baseCenter.Y + (normalY * headHalfWidth));
        var headRight = new WpfPoint(
            baseCenter.X - (normalX * headHalfWidth),
            baseCenter.Y - (normalY * headHalfWidth));

        filledHead.Visibility = isOutline ? Visibility.Collapsed : Visibility.Visible;
        filledHead.Points = new PointCollection { end, headLeft, headRight };
        filledHead.Fill = _currentBrush;
        filledHead.Stroke = _currentBrush;
        filledHead.StrokeThickness = 0d;

        outlineHead.Visibility = isOutline ? Visibility.Visible : Visibility.Collapsed;
        outlineHead.Points = new PointCollection { headLeft, end, headRight };
        outlineHead.Stroke = _currentBrush;
        outlineHead.StrokeThickness = Math.Max(2d, thickness);
        outlineHead.StrokeStartLineCap = PenLineCap.Round;
        outlineHead.StrokeEndLineCap = PenLineCap.Round;
        outlineHead.StrokeLineJoin = PenLineJoin.Round;
    }

    private static WpfRectangle CreateRectangle(
        WpfPoint start,
        WpfPoint end,
        WpfBrush? fill,
        WpfBrush? stroke,
        double strokeThickness,
        DoubleCollection? dashArray)
    {
        var rectangle = new WpfRectangle
        {
            Width = Math.Max(1d, Math.Abs(end.X - start.X)),
            Height = Math.Max(1d, Math.Abs(end.Y - start.Y)),
            Fill = fill ?? WpfBrushes.Transparent,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeDashArray = dashArray
        };
        Canvas.SetLeft(rectangle, Math.Min(start.X, end.X));
        Canvas.SetTop(rectangle, Math.Min(start.Y, end.Y));
        return rectangle;
    }

    private static Ellipse CreateEllipse(
        WpfPoint start,
        WpfPoint end,
        WpfBrush? fill,
        WpfBrush? stroke,
        double strokeThickness,
        DoubleCollection? dashArray)
    {
        var ellipse = new Ellipse
        {
            Width = Math.Max(1d, Math.Abs(end.X - start.X)),
            Height = Math.Max(1d, Math.Abs(end.Y - start.Y)),
            Fill = fill ?? WpfBrushes.Transparent,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            StrokeDashArray = dashArray
        };
        Canvas.SetLeft(ellipse, Math.Min(start.X, end.X));
        Canvas.SetTop(ellipse, Math.Min(start.Y, end.Y));
        return ellipse;
    }

    private DoubleCollection? GetDashArray()
    {
        return _strokePattern == StrokePattern.Dashed
            ? new DoubleCollection { 5d, 4d }
            : null;
    }

    private static WpfBrush CreateMosaicBrush(MosaicKind kind)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            var cell = kind == MosaicKind.Heavy ? 12 : 16;
            var baseColor = kind switch
            {
                MosaicKind.Soft => WpfColor.FromRgb(74, 74, 74),
                MosaicKind.Heavy => WpfColor.FromRgb(20, 20, 20),
                _ => WpfColor.FromRgb(42, 42, 42)
            };
            var lightColor = kind switch
            {
                MosaicKind.Heavy => WpfColor.FromRgb(64, 64, 64),
                _ => WpfColor.FromRgb(102, 102, 102)
            };
            var midColor = kind switch
            {
                MosaicKind.Heavy => WpfColor.FromRgb(38, 38, 38),
                _ => WpfColor.FromRgb(128, 128, 128)
            };
            context.DrawRectangle(new SolidColorBrush(baseColor), null, new Rect(0, 0, cell, cell));
            context.DrawRectangle(new SolidColorBrush(lightColor), null, new Rect(0, 0, cell / 2d, cell / 2d));
            context.DrawRectangle(new SolidColorBrush(midColor), null, new Rect(cell / 2d, cell / 2d, cell / 2d, cell / 2d));
        }

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, kind == MosaicKind.Heavy ? 12 : 16, kind == MosaicKind.Heavy ? 12 : 16),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        brush.Freeze();
        return brush;
    }

    private void AddTextBox(WpfPoint position)
    {
        var textBox = CreateTextEditor(position, string.Empty, 220, 20, _fontFamilyName, _fontWeight, _currentBrush);
        ShapeCanvas.Children.Add(textBox);
        textBox.Focus();
    }

    private WpfTextBox CreateTextEditor(
        WpfPoint position,
        string text,
        double width,
        double fontSize,
        string fontFamilyName,
        FontWeight fontWeight,
        WpfBrush foreground)
    {
        var textBox = new WpfTextBox
        {
            Width = Math.Max(80d, width),
            MinHeight = 36,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Text = text,
            Foreground = foreground,
            CaretBrush = foreground,
            Background = new SolidColorBrush(WpfColor.FromArgb(40, 0, 0, 0)),
            BorderBrush = foreground,
            BorderThickness = new Thickness(1),
            FontFamily = new System.Windows.Media.FontFamily(fontFamilyName),
            FontWeight = fontWeight,
            FontSize = Math.Max(12d, fontSize),
            Padding = new Thickness(8, 4, 8, 4)
        };
        textBox.ContextMenu = CreateDeleteTextMenu(textBox);
        textBox.TextChanged += (_, _) => AutoSizeTextEditor(textBox);
        textBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ConfirmTextEditor(textBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && string.IsNullOrWhiteSpace(textBox.Text))
            {
                ShapeCanvas.Children.Remove(textBox);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ShapeCanvas.Children.Remove(textBox);
                e.Handled = true;
            }
        };
        textBox.LostKeyboardFocus += (_, _) =>
        {
            if (ShapeCanvas.Children.Contains(textBox) && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                ConfirmTextEditor(textBox);
            }
        };
        Canvas.SetLeft(textBox, position.X);
        Canvas.SetTop(textBox, position.Y);
        return textBox;
    }

    private void AutoSizeTextEditor(WpfTextBox textBox)
    {
        var desiredWidth = MeasureTextEditorDesiredWidth(textBox);
        var maxWidth = GetTextEditorMaxWidth(textBox);
        textBox.Width = Math.Clamp(desiredWidth, 80d, maxWidth);
        textBox.Measure(new System.Windows.Size(textBox.Width, double.PositiveInfinity));
        textBox.Height = Math.Max(36d, Math.Ceiling(textBox.DesiredSize.Height + 2d));
    }

    private double MeasureTextEditorDesiredWidth(WpfTextBox textBox)
    {
        var lines = textBox.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var widest = 0d;
        var pixelsPerDip = VisualTreeHelper.GetDpi(textBox).PixelsPerDip;
        foreach (var line in lines)
        {
            var measuredLine = line.Length == 0 ? " " : line;
            var formattedText = new FormattedText(
                measuredLine,
                CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface(textBox.FontFamily, textBox.FontStyle, textBox.FontWeight, textBox.FontStretch),
                textBox.FontSize,
                textBox.Foreground,
                pixelsPerDip);
            widest = Math.Max(widest, formattedText.WidthIncludingTrailingWhitespace);
        }

        return Math.Ceiling(widest + textBox.Padding.Left + textBox.Padding.Right + 28d);
    }

    private double GetTextEditorMaxWidth(WpfTextBox textBox)
    {
        var left = ReadCanvasLeft(textBox);
        var surfaceWidth = EditorSurface.ActualWidth > 0 ? EditorSurface.ActualWidth : EditorSurface.Width;
        return Math.Max(80d, surfaceWidth - left - 14d);
    }

    private void ConfirmTextEditor(WpfTextBox textBox)
    {
        var text = textBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            ShapeCanvas.Children.Remove(textBox);
            return;
        }

        var display = CreateTextDisplay(textBox, text);
        Canvas.SetLeft(display, Canvas.GetLeft(textBox));
        Canvas.SetTop(display, Canvas.GetTop(textBox));
        ShapeCanvas.Children.Remove(textBox);
        ShapeCanvas.Children.Add(display);
        PushUndo(new CanvasAction(display, null));
    }

    private Canvas CreateTextDisplay(WpfTextBox source, string text)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = source.Foreground,
            FontFamily = source.FontFamily,
            FontWeight = source.FontWeight,
            FontSize = source.FontSize
        };
        var width = Math.Max(80d, source.Width);
        var height = MeasureTextObjectHeight(block, width, source.Padding);
        var container = new Canvas
        {
            Width = width,
            Height = height,
            Background = WpfBrushes.Transparent,
            Cursor = WpfCursors.IBeam,
            ClipToBounds = false
        };
        var frame = new Border
        {
            MinWidth = 40,
            Width = width,
            Height = height,
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = source.Padding,
            Child = block
        };
        container.Children.Add(frame);
        Canvas.SetLeft(frame, 0);
        Canvas.SetTop(frame, 0);

        foreach (var corner in new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight" })
        {
            var handle = CreateTextResizeHandle(corner, source.Foreground);
            container.Children.Add(handle);
        }

        container.Children.Add(CreateTextMoveHint(source.Foreground));
        PositionTextHandles(container);
        SetTextObjectChromeVisible(container, false);
        container.ContextMenu = CreateDeleteTextMenu(container);
        frame.ContextMenu = CreateDeleteTextMenu(container);
        block.ContextMenu = CreateDeleteTextMenu(container);
        container.MouseEnter += (_, _) => SetTextObjectChromeVisible(container, true);
        container.MouseLeave += (_, _) =>
        {
            if (!_isMovingTextObject && !_isResizingTextObject)
            {
                SetTextObjectChromeVisible(container, false);
            }
        };
        container.PreviewMouseLeftButtonDown += TextObject_OnPreviewMouseLeftButtonDown;
        container.MouseMove += TextObjectCursor_OnMouseMove;
        container.MouseMove += TextObject_OnMouseMove;
        container.MouseLeftButtonUp += TextObject_OnMouseLeftButtonUp;
        return container;
    }

    private void TextObject_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas textObject
            || e.OriginalSource is WpfRectangle)
        {
            return;
        }

        if (GetTextObjectFrame(textObject) is not { } frame)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            BeginMoveTextObject(textObject, e);
        }
        else
        {
            EditTextDisplay(textObject);
        }

        e.Handled = true;
    }

    private void TextObjectCursor_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (sender is not Canvas textObject || _isMovingTextObject || _isResizingTextObject)
        {
            return;
        }

        textObject.Cursor = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            ? WpfCursors.SizeAll
            : WpfCursors.IBeam;
    }

    private void EditTextDisplay(Canvas display)
    {
        if (GetTextObjectFrame(display) is not { Child: TextBlock block } frame)
        {
            return;
        }

        var position = new WpfPoint(Canvas.GetLeft(display), Canvas.GetTop(display));
        var editor = CreateTextEditor(
            position,
            block.Text,
            display.ActualWidth > 0 ? display.ActualWidth : display.Width,
            block.FontSize,
            block.FontFamily.Source,
            block.FontWeight,
            block.Foreground);
        editor.Height = Math.Max(36d, frame.ActualHeight > 0 ? frame.ActualHeight : frame.Height);
        ShapeCanvas.Children.Remove(display);
        ShapeCanvas.Children.Add(editor);
        editor.Focus();
        editor.CaretIndex = editor.Text.Length;
    }

    private WpfRectangle CreateTextResizeHandle(string corner, WpfBrush brush)
    {
        var handle = new WpfRectangle
        {
            Width = 8,
            Height = 8,
            Fill = (WpfBrush)(TryFindResource("Theme.Brush.WindowBackground") ?? WpfBrushes.Black),
            Stroke = brush,
            StrokeThickness = 1.4,
            Tag = corner,
            Cursor = corner is "TopLeft" or "BottomRight" ? WpfCursors.SizeNWSE : WpfCursors.SizeNESW
        };
        handle.MouseLeftButtonDown += TextResizeHandle_OnMouseLeftButtonDown;
        handle.MouseMove += TextObject_OnMouseMove;
        handle.MouseLeftButtonUp += TextObject_OnMouseLeftButtonUp;
        return handle;
    }

    private Border CreateTextMoveHint(WpfBrush brush)
    {
        return new Border
        {
            Tag = "TextMoveHint",
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Padding = new Thickness(7, 2, 7, 2),
            Background = (WpfBrush)(TryFindResource("Theme.Brush.SurfaceAltBackground")
                ?? new SolidColorBrush(WpfColor.FromArgb(225, 28, 30, 38))),
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new TextBlock
            {
                Text = "按住 Ctrl 拖拽移动  ·  点击编辑  ·  拖拽四角缩放  ·  右键删除",
                FontSize = 11,
                Foreground = (WpfBrush)(TryFindResource("Theme.Brush.TextPrimary") ?? WpfBrushes.White)
            }
        };
    }

    private static Border? GetTextObjectFrame(Canvas textObject)
    {
        return textObject.Children.OfType<Border>().FirstOrDefault();
    }

    private static IEnumerable<WpfRectangle> GetTextObjectHandles(Canvas textObject)
    {
        return textObject.Children.OfType<WpfRectangle>();
    }

    private static Border? GetTextObjectMoveHint(Canvas textObject)
    {
        return textObject.Children.OfType<Border>()
            .FirstOrDefault(border => string.Equals(border.Tag as string, "TextMoveHint", StringComparison.Ordinal));
    }

    private static double MeasureTextObjectHeight(TextBlock block, double width, Thickness padding)
    {
        block.Measure(new System.Windows.Size(Math.Max(1d, width - padding.Left - padding.Right), double.PositiveInfinity));
        return Math.Max(36d, Math.Ceiling(block.DesiredSize.Height + padding.Top + padding.Bottom + 2d));
    }

    private static bool IsNearFrameEdge(Border frame, WpfPoint point)
    {
        const double edgeSize = 9d;
        var width = Math.Max(1d, frame.ActualWidth > 0 ? frame.ActualWidth : frame.Width);
        var height = Math.Max(1d, frame.ActualHeight > 0 ? frame.ActualHeight : frame.Height);
        return point.X <= edgeSize
            || point.Y <= edgeSize
            || point.X >= width - edgeSize
            || point.Y >= height - edgeSize;
    }

    private void SetTextObjectChromeVisible(Canvas textObject, bool visible)
    {
        if (GetTextObjectFrame(textObject) is { } frame)
        {
            frame.BorderBrush = visible
                ? (frame.Child as TextBlock)?.Foreground ?? _currentBrush
                : WpfBrushes.Transparent;
        }

        foreach (var handle in GetTextObjectHandles(textObject))
        {
            handle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (GetTextObjectMoveHint(textObject) is { } hint)
        {
            hint.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void PositionTextHandles(Canvas textObject)
    {
        var width = Math.Max(1d, textObject.Width);
        var height = Math.Max(1d, textObject.Height);
        foreach (var handle in GetTextObjectHandles(textObject))
        {
            switch (handle.Tag as string)
            {
                case "TopLeft":
                    Canvas.SetLeft(handle, -4);
                    Canvas.SetTop(handle, -4);
                    break;
                case "TopRight":
                    Canvas.SetLeft(handle, width - 4);
                    Canvas.SetTop(handle, -4);
                    break;
                case "BottomLeft":
                    Canvas.SetLeft(handle, -4);
                    Canvas.SetTop(handle, height - 4);
                    break;
                case "BottomRight":
                    Canvas.SetLeft(handle, width - 4);
                    Canvas.SetTop(handle, height - 4);
                    break;
            }
        }

        if (GetTextObjectMoveHint(textObject) is { } hint)
        {
            hint.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(hint, 0);
            Canvas.SetTop(hint, -Math.Max(26d, hint.DesiredSize.Height + 4d));
        }
    }

    private void BeginMoveTextObject(Canvas textObject, MouseButtonEventArgs e)
    {
        _activeTextObject = textObject;
        _isMovingTextObject = true;
        _isResizingTextObject = false;
        _textDragStart = e.GetPosition(ShapeCanvas);
        _textObjectStart = new WpfPoint(ReadCanvasLeft(textObject), ReadCanvasTop(textObject));
        SetTextObjectChromeVisible(textObject, true);
        textObject.CaptureMouse();
    }

    private void TextResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not WpfRectangle { Parent: Canvas textObject, Tag: string corner })
        {
            return;
        }

        _activeTextObject = textObject;
        _isMovingTextObject = false;
        _isResizingTextObject = true;
        _textResizeCorner = corner;
        _textDragStart = e.GetPosition(ShapeCanvas);
        _textObjectStart = new WpfPoint(ReadCanvasLeft(textObject), ReadCanvasTop(textObject));
        _textObjectStartSize = new System.Windows.Size(Math.Max(1d, textObject.Width), Math.Max(1d, textObject.Height));
        _textResizeStartFontSize = GetTextObjectFrame(textObject)?.Child is TextBlock block ? block.FontSize : 20d;
        SetTextObjectChromeVisible(textObject, true);
        textObject.CaptureMouse();
        e.Handled = true;
    }

    private void TextObject_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_activeTextObject is null || (!_isMovingTextObject && !_isResizingTextObject))
        {
            return;
        }

        var current = e.GetPosition(ShapeCanvas);
        var delta = current - _textDragStart;
        if (_isMovingTextObject)
        {
            var nextLeft = ClampTextObjectLeft(_activeTextObject, _textObjectStart.X + delta.X);
            var nextTop = ClampTextObjectTop(_activeTextObject, _textObjectStart.Y + delta.Y);
            Canvas.SetLeft(_activeTextObject, nextLeft);
            Canvas.SetTop(_activeTextObject, nextTop);
            e.Handled = true;
            return;
        }

        ResizeTextObject(_activeTextObject, delta);
        e.Handled = true;
    }

    private void ResizeTextObject(Canvas textObject, Vector delta)
    {
        var horizontalDelta = _textResizeCorner.Contains("Left", StringComparison.Ordinal)
            ? -delta.X
            : delta.X;
        var verticalDelta = _textResizeCorner.Contains("Top", StringComparison.Ordinal)
            ? -delta.Y
            : delta.Y;
        var ratio = _textObjectStartSize.Width / Math.Max(1d, _textObjectStartSize.Height);
        var widthFromX = _textObjectStartSize.Width + horizontalDelta;
        var widthFromY = (_textObjectStartSize.Height + verticalDelta) * ratio;
        var targetWidth = Math.Max(48d, Math.Abs(horizontalDelta) >= Math.Abs(verticalDelta) ? widthFromX : widthFromY);
        var scale = targetWidth / Math.Max(1d, _textObjectStartSize.Width);
        var targetHeight = Math.Max(28d, _textObjectStartSize.Height * scale);

        if (_textResizeCorner.Contains("Left", StringComparison.Ordinal))
        {
            Canvas.SetLeft(textObject, _textObjectStart.X + (_textObjectStartSize.Width - targetWidth));
        }

        if (_textResizeCorner.Contains("Top", StringComparison.Ordinal))
        {
            Canvas.SetTop(textObject, _textObjectStart.Y + (_textObjectStartSize.Height - targetHeight));
        }

        ApplyTextObjectSize(textObject, targetWidth, targetHeight, Math.Max(8d, _textResizeStartFontSize * scale));
    }

    private static void ApplyTextObjectSize(Canvas textObject, double width, double height, double fontSize)
    {
        textObject.Width = width;
        textObject.Height = height;
        if (GetTextObjectFrame(textObject) is { Child: TextBlock block } frame)
        {
            frame.Width = width;
            frame.Height = height;
            block.FontSize = fontSize;
        }

        PositionTextHandles(textObject);
    }

    private double ClampTextObjectLeft(Canvas textObject, double left)
    {
        var surfaceWidth = EditorSurface.ActualWidth > 0 ? EditorSurface.ActualWidth : EditorSurface.Width;
        return Math.Clamp(left, 0d, Math.Max(0d, surfaceWidth - textObject.Width));
    }

    private double ClampTextObjectTop(Canvas textObject, double top)
    {
        var surfaceHeight = EditorSurface.ActualHeight > 0 ? EditorSurface.ActualHeight : EditorSurface.Height;
        return Math.Clamp(top, 0d, Math.Max(0d, surfaceHeight - textObject.Height));
    }

    private void TextObject_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeTextObject is not null)
        {
            _activeTextObject.ReleaseMouseCapture();
        }

        _isMovingTextObject = false;
        _isResizingTextObject = false;
        _activeTextObject = null;
        _textResizeCorner = string.Empty;
        e.Handled = true;
    }

    private static double ReadCanvasLeft(UIElement element)
    {
        var value = Canvas.GetLeft(element);
        return double.IsNaN(value) ? 0d : value;
    }

    private static double ReadCanvasTop(UIElement element)
    {
        var value = Canvas.GetTop(element);
        return double.IsNaN(value) ? 0d : value;
    }

    private ContextMenu CreateDeleteTextMenu(FrameworkElement target)
    {
        var menu = new ContextMenu
        {
            Padding = new Thickness(6),
            Background = (WpfBrush)(TryFindResource("Theme.Brush.WindowBackground") ?? WpfBrushes.Black),
            BorderBrush = (WpfBrush)(TryFindResource("Theme.Brush.WindowBorder") ?? WpfBrushes.Gray),
            BorderThickness = new Thickness(1),
            Template = CreateRoundedContextMenuTemplate()
        };
        var deleteItem = new MenuItem
        {
            Header = "删除文本",
            Foreground = (WpfBrush)(TryFindResource("Theme.Brush.TextPrimary") ?? WpfBrushes.White),
            Background = WpfBrushes.Transparent,
            Template = CreateRoundedMenuItemTemplate()
        };
        deleteItem.Click += (_, _) => ShapeCanvas.Children.Remove(target);
        menu.Items.Add(deleteItem);
        return menu;
    }

    private static ControlTemplate CreateRoundedContextMenuTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetValue(Border.PaddingProperty, new Thickness(6));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(ContextMenu.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(ContextMenu.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(ContextMenu.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        presenter.SetValue(KeyboardNavigation.DirectionalNavigationProperty, KeyboardNavigationMode.Cycle);
        border.AppendChild(presenter);

        return new ControlTemplate(typeof(ContextMenu))
        {
            VisualTree = border
        };
    }

    private ControlTemplate CreateRoundedMenuItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border))
        {
            Name = "ItemBorder"
        };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, WpfBrushes.Transparent);
        border.SetValue(Border.PaddingProperty, new Thickness(14, 8, 14, 8));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        presenter.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new TemplateBindingExtension(MenuItem.ForegroundProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(MenuItem))
        {
            VisualTree = border
        };
        var hover = new Trigger
        {
            Property = MenuItem.IsHighlightedProperty,
            Value = true
        };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            (WpfBrush)(TryFindResource("Theme.Brush.ButtonHoverBackground") ?? new SolidColorBrush(WpfColor.FromArgb(80, 255, 255, 255))),
            "ItemBorder"));
        template.Triggers.Add(hover);
        return template;
    }

    private void InkLayer_OnStrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        PushUndo(new CanvasAction(null, e.Stroke));
    }

    private void UndoButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        if (_undoStack.TryPop(out var action))
        {
            action.Remove(ShapeCanvas, InkLayer);
            _redoStack.Push(action);
        }
    }

    private void RedoButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        if (_redoStack.TryPop(out var action))
        {
            action.Add(ShapeCanvas, InkLayer);
            _undoStack.Push(action);
        }
    }

    private void PushUndo(CanvasAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
    }

    private void ColorComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CloseColorPaletteWindow();
        _colorSourceKind = ColorSourceKind.Theme;
        UpdateColorSourceUi();
        if (ColorComboBox.SelectedItem is ComboBoxItem { Tag: string value })
        {
            ApplyColor(ResolveColorTag(value));
            UpdateDrawingAttributes();
        }
    }

    private void ThemeColorSourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        _colorSourceKind = ColorSourceKind.Theme;
        UpdateColorSourceUi();
        if (ColorComboBox.SelectedItem is ComboBoxItem { Tag: string value })
        {
            ApplyColor(ResolveColorTag(value));
            UpdateDrawingAttributes();
        }
    }

    private void PaletteColorSourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        _colorSourceKind = ColorSourceKind.Palette;
        UpdateColorSourceUi();
        ShowColorPaletteWindow();
    }

    private void CustomColorButton_OnClick(object sender, RoutedEventArgs e)
    {
        _colorSourceKind = ColorSourceKind.Palette;
        UpdateColorSourceUi();
        ShowColorPaletteWindow();
    }

    private void UpdateColorSourceUi()
    {
        if (ColorSourcePanel is null
            || ThemeColorSourceButton is null
            || PaletteColorSourceButton is null
            || ColorComboBox is null
            || CustomColorButton is null)
        {
            return;
        }

        var showColor = UsesColor(_currentTool);
        ColorSourcePanel.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        ThemeColorSourceButton.Background = _colorSourceKind == ColorSourceKind.Theme
            ? (WpfBrush)(TryFindResource("Theme.Brush.Accent") ?? WpfBrushes.DeepPink)
            : (WpfBrush)(TryFindResource("Theme.Brush.ButtonBackground") ?? WpfBrushes.Transparent);
        PaletteColorSourceButton.Background = _colorSourceKind == ColorSourceKind.Palette
            ? (WpfBrush)(TryFindResource("Theme.Brush.Accent") ?? WpfBrushes.DeepPink)
            : (WpfBrush)(TryFindResource("Theme.Brush.ButtonBackground") ?? WpfBrushes.Transparent);
        ThemeColorSourceButton.BorderBrush = _colorSourceKind == ColorSourceKind.Theme
            ? (WpfBrush)(TryFindResource("Theme.Brush.AccentBorder") ?? WpfBrushes.DeepPink)
            : (WpfBrush)(TryFindResource("Theme.Brush.ButtonBorder") ?? WpfBrushes.Gray);
        PaletteColorSourceButton.BorderBrush = _colorSourceKind == ColorSourceKind.Palette
            ? (WpfBrush)(TryFindResource("Theme.Brush.AccentBorder") ?? WpfBrushes.DeepPink)
            : (WpfBrush)(TryFindResource("Theme.Brush.ButtonBorder") ?? WpfBrushes.Gray);
        ColorComboBox.Visibility = showColor && _colorSourceKind == ColorSourceKind.Theme
            ? Visibility.Visible
            : Visibility.Collapsed;
        CustomColorButton.Visibility = showColor && _colorSourceKind == ColorSourceKind.Palette
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShowColorPaletteWindow()
    {
        CloseColorPaletteWindow();
        var palette = new[]
        {
            "#FFFFFFFF", "#FF000000", "#FFEF4444", "#FFF97316", "#FFFACC15", "#FF22C55E",
            "#FF06B6D4", "#FF3B82F6", "#FF8B5CF6", "#FFEC4899", "#FF64748B", "#FF94A3B8"
        };

        var panel = new WrapPanel
        {
            Width = 184,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var popup = new Window
        {
            Owner = this,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = WpfBrushes.Transparent,
            Topmost = true
        };
        _colorPaletteWindow = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_colorPaletteWindow, popup))
            {
                _colorPaletteWindow = null;
            }
        };

        foreach (var value in palette)
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(value);
            var swatch = new WpfButton
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(4),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(150, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Cursor = WpfCursors.Hand,
                ToolTip = value
            };
            swatch.Click += (_, _) =>
            {
                ApplyColor(color);
                UpdateDrawingAttributes();
                popup.Close();
            };
            panel.Children.Add(swatch);
        }

        var colorMap = CreateSpectrumColorMap(popup);

        var card = new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(18),
            Background = (WpfBrush)(TryFindResource("Theme.Brush.WindowBackground") ?? new SolidColorBrush(WpfColor.FromArgb(245, 17, 24, 39))),
            BorderBrush = (WpfBrush)(TryFindResource("Theme.Brush.WindowBorder") ?? WpfBrushes.Gray),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "选择色板颜色",
                        Foreground = (WpfBrush)(TryFindResource("Theme.Brush.TextPrimary") ?? WpfBrushes.White),
                        FontWeight = FontWeights.SemiBold
                    },
                    panel,
                    colorMap
                }
            }
        };
        popup.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed
                && e.OriginalSource is DependencyObject source
                && !IsDescendantOf<WpfButton>(source, card)
                && !IsDescendantOf(source, colorMap))
            {
                popup.DragMove();
                e.Handled = true;
            }
        };
        popup.Content = card;
        popup.Loaded += (_, _) => PositionColorPaletteWindow(popup);
        popup.Show();
    }

    private Grid CreateSpectrumColorMap(Window popup)
    {
        var map = new Grid
        {
            Width = 184,
            Height = 68,
            Margin = new Thickness(4, 10, 4, 0),
            Cursor = WpfCursors.Cross
        };
        map.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = CreateSpectrumBrush()
        });
        map.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush(
                WpfColor.FromArgb(0, 0, 0, 0),
                WpfColor.FromArgb(170, 0, 0, 0),
                90d)
        });
        map.MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(map);
            ApplyColor(PickSpectrumColor(point, map.ActualWidth, map.ActualHeight));
            UpdateDrawingAttributes();
            popup.Close();
        };
        return map;
    }

    private static LinearGradientBrush CreateSpectrumBrush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new WpfPoint(0, 0.5),
            EndPoint = new WpfPoint(1, 0.5),
            GradientStops =
            {
                new GradientStop(WpfColor.FromRgb(255, 0, 0), 0.00),
                new GradientStop(WpfColor.FromRgb(255, 255, 0), 0.17),
                new GradientStop(WpfColor.FromRgb(0, 255, 0), 0.33),
                new GradientStop(WpfColor.FromRgb(0, 255, 255), 0.50),
                new GradientStop(WpfColor.FromRgb(0, 0, 255), 0.67),
                new GradientStop(WpfColor.FromRgb(255, 0, 255), 0.83),
                new GradientStop(WpfColor.FromRgb(255, 0, 0), 1.00)
            }
        };
    }

    private static WpfColor PickSpectrumColor(WpfPoint point, double width, double height)
    {
        var hue = Math.Clamp(point.X / Math.Max(1d, width), 0d, 1d) * 360d;
        var value = 1d - (Math.Clamp(point.Y / Math.Max(1d, height), 0d, 1d) * 0.72d);
        return HsvToRgb(hue, 1d, Math.Clamp(value, 0.18d, 1d));
    }

    private static WpfColor HsvToRgb(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60d % 2) - 1));
        var m = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        return WpfColor.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private void PositionColorPaletteWindow(Window popup)
    {
        popup.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var anchorTopLeftPx = CustomColorButton.PointToScreen(new WpfPoint(0, 0));
        var anchorBottomRightPx = CustomColorButton.PointToScreen(
            new WpfPoint(CustomColorButton.ActualWidth, CustomColorButton.ActualHeight));
        var anchor = new Rect(
            anchorTopLeftPx.X / dpi.DpiScaleX,
            anchorTopLeftPx.Y / dpi.DpiScaleY,
            Math.Max(1d, (anchorBottomRightPx.X - anchorTopLeftPx.X) / dpi.DpiScaleX),
            Math.Max(1d, (anchorBottomRightPx.Y - anchorTopLeftPx.Y) / dpi.DpiScaleY));
        var screen = FormsScreen.FromPoint(new System.Drawing.Point(
            (int)Math.Round(anchorTopLeftPx.X),
            (int)Math.Round(anchorTopLeftPx.Y)));
        var workArea = new Rect(
            screen.WorkingArea.Left / dpi.DpiScaleX,
            screen.WorkingArea.Top / dpi.DpiScaleY,
            screen.WorkingArea.Width / dpi.DpiScaleX,
            screen.WorkingArea.Height / dpi.DpiScaleY);
        var popupWidth = Math.Max(1d, popup.ActualWidth);
        var popupHeight = Math.Max(1d, popup.ActualHeight);
        const double gap = 6d;

        var candidates = new[]
        {
            new WpfPoint(anchor.Left, anchor.Bottom + gap),
            new WpfPoint(anchor.Left, anchor.Top - popupHeight - gap),
            new WpfPoint(anchor.Right + gap, anchor.Top),
            new WpfPoint(anchor.Left - popupWidth - gap, anchor.Top)
        };
        var position = candidates.FirstOrDefault(candidate =>
            candidate.X >= workArea.Left
            && candidate.Y >= workArea.Top
            && candidate.X + popupWidth <= workArea.Right
            && candidate.Y + popupHeight <= workArea.Bottom);
        if (position == default)
        {
            position = candidates[0];
        }

        popup.Left = Math.Clamp(position.X, workArea.Left + gap, Math.Max(workArea.Left + gap, workArea.Right - popupWidth - gap));
        popup.Top = Math.Clamp(position.Y, workArea.Top + gap, Math.Max(workArea.Top + gap, workArea.Bottom - popupHeight - gap));
    }

    private static bool IsDescendantOf<T>(DependencyObject source, DependencyObject boundary)
        where T : DependencyObject
    {
        for (DependencyObject? current = source; current is not null && !ReferenceEquals(current, boundary); current = GetParent(current))
        {
            if (current is T)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDescendantOf(DependencyObject source, DependencyObject target)
    {
        for (DependencyObject? current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject source)
    {
        return source switch
        {
            Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(source),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(source)
        };
    }

    private void CloseColorPaletteWindow()
    {
        if (_colorPaletteWindow is { IsVisible: true } window)
        {
            window.Close();
        }
        _colorPaletteWindow = null;
    }

    private WpfColor ResolveColorTag(string value)
    {
        return string.Equals(value, "ThemeAccent", StringComparison.OrdinalIgnoreCase)
            ? GetThemeAccentColor()
            : (WpfColor)WpfColorConverter.ConvertFromString(value);
    }

    private WpfColor GetThemeAccentColor()
    {
        return TryFindResource("Theme.Color.Accent") is WpfColor color
            ? color
            : System.Windows.Media.Colors.DeepPink;
    }

    private void ApplyColor(WpfColor color)
    {
        _currentColor = color;
        _currentBrush = new SolidColorBrush(_currentColor);
        _currentBrush.Freeze();

        if (BrushCursorRing is not null)
        {
            BrushCursorLeftLine.Stroke = _currentBrush;
            BrushCursorRightLine.Stroke = _currentBrush;
            BrushCursorTopLine.Stroke = _currentBrush;
            BrushCursorBottomLine.Stroke = _currentBrush;
            BrushCursorRing.Stroke = _currentBrush;
        }
    }

    private void ThicknessSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InkLayer is not null)
        {
            UpdateDrawingAttributes();
        }
    }

    private void ToolOption_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StrokePatternComboBox is null
            || ShapeKindComboBox is null
            || ArrowKindComboBox is null
            || MosaicKindComboBox is null
            || MosaicApplicationModeComboBox is null
            || FontFamilyComboBox is null
            || FontWeightComboBox is null)
        {
            return;
        }

        _strokePattern = ReadEnumOption(StrokePatternComboBox, StrokePattern.Solid);
        _selectionShapeKind = ReadEnumOption(ShapeKindComboBox, SelectionShapeKind.Rectangle);
        _lineArrowKind = ReadEnumOption(ArrowKindComboBox, LineArrowKind.Line);
        _mosaicKind = ReadEnumOption(MosaicKindComboBox, MosaicKind.Block);
        _mosaicApplicationMode = ReadEnumOption(MosaicApplicationModeComboBox, MosaicApplicationMode.Rectangle);
        _fontFamilyName = ReadTagOption(FontFamilyComboBox, "Microsoft YaHei UI");
        _fontWeight = string.Equals(ReadTagOption(FontWeightComboBox, "Normal"), "Bold", StringComparison.Ordinal)
            ? FontWeights.Bold
            : FontWeights.Normal;
        UpdateDrawingAttributes();
        UpdateToolOptionsVisibility();
        HideBrushCursor();
    }

    private static T ReadEnumOption<T>(WpfComboBox comboBox, T fallback)
        where T : struct, Enum
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<T>(tag, out var value)
                ? value
                : fallback;
    }

    private static string ReadTagOption(WpfComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string tag } && !string.IsNullOrWhiteSpace(tag)
            ? tag
            : fallback;
    }

    private void UpdateToolButtonStates()
    {
        foreach (var button in GetToolButtons())
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
            button.ClearValue(ForegroundProperty);

            if (button.Tag is string tag
                && Enum.TryParse<EditorTool>(tag, out var tool)
                && tool == _currentTool)
            {
                button.Background = (WpfBrush)(TryFindResource("Theme.Brush.Accent") ?? WpfBrushes.DeepPink);
                button.BorderBrush = (WpfBrush)(TryFindResource("Theme.Brush.AccentBorder") ?? WpfBrushes.DeepPink);
                button.Foreground = (WpfBrush)(TryFindResource("Theme.Brush.TextPrimary") ?? WpfBrushes.White);
            }
        }
    }

    private IEnumerable<WpfButton> GetToolButtons()
    {
        yield return SelectToolButton;
        yield return LineToolButton;
        yield return ArrowToolButton;
        yield return PenToolButton;
        yield return MarkerToolButton;
        yield return MosaicToolButton;
        yield return EraserToolButton;
        yield return TextToolButton;
    }

    private void UpdateToolOptionsVisibility()
    {
        if (StrokePatternPanel is null)
        {
            return;
        }

        var showColor = UsesColor(_currentTool);
        var showThickness = UsesThickness(_currentTool)
            || (_currentTool == EditorTool.Mosaic && _mosaicApplicationMode == MosaicApplicationMode.Brush);
        var isMosaicBrush = _currentTool == EditorTool.Mosaic
            && _mosaicApplicationMode == MosaicApplicationMode.Brush;
        ThicknessLabel.Text = isMosaicBrush ? "画笔直径" : "粗细";
        var minimumThickness = isMosaicBrush ? 8d : 2d;
        var maximumThickness = isMosaicBrush ? 96d : 36d;
        ThicknessSlider.Minimum = minimumThickness;
        ThicknessSlider.Maximum = maximumThickness;
        ThicknessSlider.Value = Math.Clamp(ThicknessSlider.Value, minimumThickness, maximumThickness);
        ColorLabel.Visibility = showColor ? Visibility.Visible : Visibility.Collapsed;
        ThicknessLabel.Visibility = showThickness ? Visibility.Visible : Visibility.Collapsed;
        ThicknessSlider.Visibility = showThickness ? Visibility.Visible : Visibility.Collapsed;
        StrokePatternPanel.Visibility = _currentTool is EditorTool.Select or EditorTool.Line ? Visibility.Visible : Visibility.Collapsed;
        ShapeKindPanel.Visibility = _currentTool == EditorTool.Select ? Visibility.Visible : Visibility.Collapsed;
        ArrowKindPanel.Visibility = _currentTool == EditorTool.Line ? Visibility.Visible : Visibility.Collapsed;
        MosaicKindPanel.Visibility = _currentTool == EditorTool.Mosaic ? Visibility.Visible : Visibility.Collapsed;
        FontPanel.Visibility = _currentTool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        UpdateColorSourceUi();
    }

    private static bool UsesColor(EditorTool tool)
    {
        return tool is EditorTool.Select or EditorTool.Line or EditorTool.Arrow or EditorTool.Pen or EditorTool.Marker or EditorTool.Text;
    }

    private static bool UsesThickness(EditorTool tool)
    {
        return tool is EditorTool.Select or EditorTool.Line or EditorTool.Arrow or EditorTool.Pen or EditorTool.Marker or EditorTool.Eraser;
    }

    private void ClearCanvasButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        if (ShapeCanvas.Children.Count == 0 && InkLayer.Strokes.Count == 0)
        {
            return;
        }

        var elements = ShapeCanvas.Children.Cast<FrameworkElement>().ToList();
        var strokes = InkLayer.Strokes.ToList();
        ShapeCanvas.Children.Clear();
        InkLayer.Strokes.Clear();
        _undoStack.Push(CanvasAction.CreateClear(elements, strokes));
        _redoStack.Clear();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        var tempPath = RenderToTempFile();
        if (_mode == CanvasEditorMode.FullScreenCanvas)
        {
            SavedPath = _app.CapturedImageFileService.SaveToDefaultDirectory(tempPath);
            CopyImageToClipboard(SavedPath);
        }
        else if (_saveAsAndCopyOnConfirm)
        {
            SavedPath = tempPath;
            CopyImageToClipboard(SavedPath);
        }
        else
        {
            SavedPath = tempPath;
        }

        DialogResult = true;
        Close();
    }

    private void SaveAsButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        var tempPath = RenderToTempFile();
        var savedPath = _app.CapturedImageFileService.SaveAs(tempPath);
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return;
        }

        SavedPath = savedPath;
        DialogResult = true;
        Close();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseColorPaletteWindow();
        DialogResult = false;
        Close();
    }

    private static void CopyImageToClipboard(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        Clipboard.SetImage(LoadBitmap(imagePath));
    }

    private void Window_OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoButton_OnClick(this, new RoutedEventArgs());
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RedoButton_OnClick(this, new RoutedEventArgs());
        }
    }

    private void Toolbar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed
            && e.OriginalSource is DependencyObject source
            && !IsToolbarControl(source))
        {
            _isToolbarDragging = true;
            _toolbarDragStart = e.GetPosition(RootGrid);
            _toolbarDragOriginMargin = Toolbar.Margin;
            Toolbar.CaptureMouse();
            e.Handled = true;
        }
    }

    private bool IsToolbarControl(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null && !ReferenceEquals(current, Toolbar); current = GetParent(current))
        {
            if (current is WpfButton or Slider or WpfComboBox)
            {
                return true;
            }
        }

        return false;
    }

    private void Toolbar_OnMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_isToolbarDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(RootGrid);
        var deltaX = current.X - _toolbarDragStart.X;
        var deltaY = current.Y - _toolbarDragStart.Y;
        SetToolbarPosition(
            new WpfPoint(_toolbarDragOriginMargin.Left + deltaX, _toolbarDragOriginMargin.Top + deltaY),
            Math.Max(1d, Toolbar.ActualWidth),
            Math.Max(1d, Toolbar.ActualHeight));
        e.Handled = true;
    }

    private void Toolbar_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isToolbarDragging)
        {
            return;
        }

        _isToolbarDragging = false;
        Toolbar.ReleaseMouseCapture();
        e.Handled = true;
    }

    private string RenderToTempFile()
    {
        EditorSurface.UpdateLayout();
        var width = Math.Max(1, (int)Math.Round(EditorSurface.ActualWidth));
        var height = Math.Max(1, (int)Math.Round(EditorSurface.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(EditorSurface);
        bitmap.Freeze();

        var tempDirectory = _app.CapturedImageFileService.GetTempDirectoryPath();
        Directory.CreateDirectory(tempDirectory);
        var outputPath = System.IO.Path.Combine(tempDirectory, $"canvas-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
        return outputPath;
    }

    private static BitmapSource LoadBitmap(string imagePath)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(imagePath);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static WpfPoint GetDpiScale()
    {
        var source = PresentationSource.FromVisual(WpfApplication.Current.MainWindow);
        if (source?.CompositionTarget is null)
        {
            return new WpfPoint(1, 1);
        }

        var matrix = source.CompositionTarget.TransformToDevice;
        return new WpfPoint(matrix.M11 <= 0 ? 1 : matrix.M11, matrix.M22 <= 0 ? 1 : matrix.M22);
    }

    private sealed record CanvasAction(
        FrameworkElement? Element,
        Stroke? Stroke,
        IReadOnlyList<FrameworkElement>? ClearedElements = null,
        IReadOnlyList<Stroke>? ClearedStrokes = null)
    {
        public static CanvasAction CreateClear(
            IReadOnlyList<FrameworkElement> elements,
            IReadOnlyList<Stroke> strokes)
        {
            return new CanvasAction(null, null, elements, strokes);
        }

        public void Add(Canvas canvas, InkCanvas inkCanvas)
        {
            if (ClearedElements is not null || ClearedStrokes is not null)
            {
                canvas.Children.Clear();
                inkCanvas.Strokes.Clear();
                return;
            }

            if (Element is not null && !canvas.Children.Contains(Element))
            {
                canvas.Children.Add(Element);
            }

            if (Stroke is not null && !inkCanvas.Strokes.Contains(Stroke))
            {
                inkCanvas.Strokes.Add(Stroke);
            }
        }

        public void Remove(Canvas canvas, InkCanvas inkCanvas)
        {
            if (ClearedElements is not null || ClearedStrokes is not null)
            {
                foreach (var element in ClearedElements ?? [])
                {
                    if (!canvas.Children.Contains(element))
                    {
                        canvas.Children.Add(element);
                    }
                }

                foreach (var stroke in ClearedStrokes ?? [])
                {
                    if (!inkCanvas.Strokes.Contains(stroke))
                    {
                        inkCanvas.Strokes.Add(stroke);
                    }
                }

                return;
            }

            if (Element is not null)
            {
                canvas.Children.Remove(Element);
            }

            if (Stroke is not null)
            {
                inkCanvas.Strokes.Remove(Stroke);
            }
        }
    }

    private sealed class MosaicBrushStroke
    {
        public GeometryGroup Mask { get; } = new() { FillRule = FillRule.Nonzero };

        public GeometryGroup EraseMask { get; } = new() { FillRule = FillRule.Nonzero };

        public WpfImage? Image { get; set; }

        public WpfPoint LastPoint { get; set; }

        public bool HasLastPoint { get; set; }
    }

    private sealed class MosaicImageState
    {
        public Rect Bounds { get; set; }

        public GeometryGroup EraseMask { get; } = new() { FillRule = FillRule.Nonzero };
    }
}
