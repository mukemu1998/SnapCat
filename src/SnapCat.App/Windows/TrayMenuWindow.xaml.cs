using System.Drawing;
using System.Windows;
using System.Windows.Media;
using SnapCat.Core.Models;
using DrawingPoint = System.Drawing.Point;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class TrayMenuWindow : Window
{
    private readonly Action<CaptureWorkflowKind> _startCaptureAction;
    private readonly Action<CaptureWorkflowKind> _setTrayLeftClickAction;
    private readonly Action _showMainWindowAction;
    private readonly Action _openHistoryAction;
    private readonly Action _openSettingsAction;
    private readonly Action _openCaptureDirectoryAction;
    private readonly Action _exitAction;
    private DrawingPoint _cursorPosition;

    public TrayMenuWindow(
        CaptureWorkflowKind currentLeftClickAction,
        Action<CaptureWorkflowKind> startCaptureAction,
        Action<CaptureWorkflowKind> setTrayLeftClickAction,
        Action showMainWindowAction,
        Action openHistoryAction,
        Action openSettingsAction,
        Action openCaptureDirectoryAction,
        Action exitAction)
    {
        InitializeComponent();
        _startCaptureAction = startCaptureAction;
        _setTrayLeftClickAction = setTrayLeftClickAction;
        _showMainWindowAction = showMainWindowAction;
        _openHistoryAction = openHistoryAction;
        _openSettingsAction = openSettingsAction;
        _openCaptureDirectoryAction = openCaptureDirectoryAction;
        _exitAction = exitAction;

        Loaded += TrayMenuWindow_OnLoaded;
        Deactivated += (_, _) => Close();
        PreviewKeyDown += TrayMenuWindow_OnPreviewKeyDown;

        UpdateCurrentAction(currentLeftClickAction);
    }

    public void ShowAt(DrawingPoint cursorPosition)
    {
        _cursorPosition = cursorPosition;
        Show();
        Activate();
    }

    private void TrayMenuWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLayout();

        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var workAreaPx = FormsScreen.FromPoint(_cursorPosition).WorkingArea;
        var workAreaTopLeft = fromDevice.Transform(new WpfPoint(workAreaPx.Left, workAreaPx.Top));
        var workAreaBottomRight = fromDevice.Transform(new WpfPoint(workAreaPx.Right, workAreaPx.Bottom));
        var workAreaDip = new WpfRect(workAreaTopLeft, workAreaBottomRight);

        var cursorDip = fromDevice.Transform(new WpfPoint(_cursorPosition.X, _cursorPosition.Y));
        var width = ActualWidth;
        var height = ActualHeight;
        var left = Math.Max(workAreaDip.Left + 8, Math.Min(cursorDip.X - width + 12, workAreaDip.Right - width - 8));
        var top = cursorDip.Y - height - 8;

        if (top < workAreaDip.Top + 8)
        {
            top = Math.Min(cursorDip.Y + 12, workAreaDip.Bottom - height - 8);
        }

        Left = left;
        Top = top;
    }

    private void TrayMenuWindow_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Close();
        }
    }

    private void UpdateCurrentAction(CaptureWorkflowKind currentAction)
    {
        PinActionIndicator.Text = currentAction == CaptureWorkflowKind.CaptureAndPin ? "当前" : string.Empty;
        TranslateActionIndicator.Text = currentAction == CaptureWorkflowKind.CaptureAndTranslate ? "当前" : string.Empty;
        WaitActionIndicator.Text = currentAction == CaptureWorkflowKind.CaptureAndWaitForAction ? "当前" : string.Empty;
        CurrentActionSummaryTextBlock.Text = $"左键托盘默认执行：{GetActionLabel(currentAction)}";
    }

    private static string GetActionLabel(CaptureWorkflowKind action)
    {
        return action switch
        {
            CaptureWorkflowKind.CaptureAndPin => "固定到屏幕",
            CaptureWorkflowKind.CaptureAndTranslate => "自动翻译",
            _ => "等待选择"
        };
    }

    private void ExecuteAndClose(Action action)
    {
        action();
        Close();
    }

    private void CaptureAndPinButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _startCaptureAction(CaptureWorkflowKind.CaptureAndPin));
    }

    private void CaptureAndTranslateButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _startCaptureAction(CaptureWorkflowKind.CaptureAndTranslate));
    }

    private void CaptureAndWaitButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _startCaptureAction(CaptureWorkflowKind.CaptureAndWaitForAction));
    }

    private void PinActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _setTrayLeftClickAction(CaptureWorkflowKind.CaptureAndPin));
    }

    private void TranslateActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _setTrayLeftClickAction(CaptureWorkflowKind.CaptureAndTranslate));
    }

    private void WaitActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _setTrayLeftClickAction(CaptureWorkflowKind.CaptureAndWaitForAction));
    }

    private void ShowMainWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_showMainWindowAction);
    }

    private void OpenHistoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_openHistoryAction);
    }

    private void OpenSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_openSettingsAction);
    }

    private void OpenCaptureDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_openCaptureDirectoryAction);
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_exitAction);
    }
}
