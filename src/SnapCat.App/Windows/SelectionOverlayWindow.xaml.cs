using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using Point = System.Windows.Point;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SnapCat.App.Windows;

public partial class SelectionOverlayWindow : Window
{
    private readonly DrawingRectangle _virtualScreenBounds;
    private Point? _startPoint;
    private Matrix _fromDevice = Matrix.Identity;
    private Matrix _toDevice = Matrix.Identity;

    public SelectionOverlayWindow()
    {
        InitializeComponent();

        _virtualScreenBounds = SystemInformation.VirtualScreen;
        Loaded += SelectionOverlayWindow_OnLoaded;
    }

    public Int32Rect? SelectedRegion { get; private set; }

    private void SelectionOverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        InitializeTransforms();

        var topLeftDip = _fromDevice.Transform(new Point(_virtualScreenBounds.Left, _virtualScreenBounds.Top));
        var bottomRightDip = _fromDevice.Transform(new Point(_virtualScreenBounds.Right, _virtualScreenBounds.Bottom));

        Left = topLeftDip.X;
        Top = topLeftDip.Y;
        Width = bottomRightDip.X - topLeftDip.X;
        Height = bottomRightDip.Y - topLeftDip.Y;
    }

    private void RootSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(RootSurface);
        RootSurface.CaptureMouse();

        Canvas.SetLeft(SelectionRectangle, _startPoint.Value.X);
        Canvas.SetTop(SelectionRectangle, _startPoint.Value.Y);
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        SelectionRectangle.Visibility = Visibility.Visible;
    }

    private void RootSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_startPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(RootSurface);
        UpdateSelection(_startPoint.Value, current);
    }

    private void RootSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_startPoint is null)
        {
            return;
        }

        var endPoint = e.GetPosition(RootSurface);
        RootSurface.ReleaseMouseCapture();
        UpdateSelection(_startPoint.Value, endPoint);

        var left = Canvas.GetLeft(SelectionRectangle);
        var top = Canvas.GetTop(SelectionRectangle);
        var width = SelectionRectangle.Width;
        var height = SelectionRectangle.Height;

        _startPoint = null;

        if (width < 8 || height < 8)
        {
            DialogResult = false;
            return;
        }

        var topLeftPx = _toDevice.Transform(new Point(left, top));
        var bottomRightPx = _toDevice.Transform(new Point(left + width, top + height));

        SelectedRegion = new Int32Rect(
            _virtualScreenBounds.Left + (int)Math.Round(topLeftPx.X),
            _virtualScreenBounds.Top + (int)Math.Round(topLeftPx.Y),
            (int)Math.Round(bottomRightPx.X - topLeftPx.X),
            (int)Math.Round(bottomRightPx.Y - topLeftPx.Y));

        DialogResult = true;
    }

    private void UpdateSelection(Point start, Point current)
    {
        var x = Math.Min(start.X, current.X);
        var y = Math.Min(start.Y, current.Y);
        var width = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private void InitializeTransforms()
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not null)
        {
            _fromDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformFromDevice;
            _toDevice = PresentationSource.FromVisual(this)!.CompositionTarget.TransformToDevice;
        }
    }
}
