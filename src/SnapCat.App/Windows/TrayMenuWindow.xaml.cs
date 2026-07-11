using System.Drawing;
using System.Windows;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using DrawingPoint = System.Drawing.Point;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class TrayMenuWindow : Window
{
    private readonly Action _showMainWindowAction;
    private readonly Action _openHistoryAction;
    private readonly Action _openSettingsAction;
    private readonly Action _openCaptureDirectoryAction;
    private readonly Action _showAllPinnedWindowsAction;
    private readonly Action _hideAllPinnedWindowsAction;
    private readonly Action _showUngroupedPinnedWindowsAction;
    private readonly Action<string> _showPinnedGroupAction;
    private readonly Action _exitAction;
    private DrawingPoint _cursorPosition;
    private bool _closeRequested;

    public TrayMenuWindow(
        AppSettings settings,
        Action showMainWindowAction,
        Action openHistoryAction,
        Action openSettingsAction,
        Action openCaptureDirectoryAction,
        Action showAllPinnedWindowsAction,
        Action hideAllPinnedWindowsAction,
        Action showUngroupedPinnedWindowsAction,
        Action<string> showPinnedGroupAction,
        Action exitAction)
    {
        InitializeComponent();
        _showMainWindowAction = showMainWindowAction;
        _openHistoryAction = openHistoryAction;
        _openSettingsAction = openSettingsAction;
        _openCaptureDirectoryAction = openCaptureDirectoryAction;
        _showAllPinnedWindowsAction = showAllPinnedWindowsAction;
        _hideAllPinnedWindowsAction = hideAllPinnedWindowsAction;
        _showUngroupedPinnedWindowsAction = showUngroupedPinnedWindowsAction;
        _showPinnedGroupAction = showPinnedGroupAction;
        _exitAction = exitAction;

        Loaded += TrayMenuWindow_OnLoaded;
        Deactivated += TrayMenuWindow_OnDeactivated;
        Closing += (_, _) => _closeRequested = true;
        PreviewKeyDown += TrayMenuWindow_OnPreviewKeyDown;

        UpdateShortcutHints(settings);
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
            RequestClose();
        }
    }

    private void TrayMenuWindow_OnDeactivated(object? sender, EventArgs e)
    {
        RequestClose();
    }

    private void UpdateShortcutHints(AppSettings settings)
    {
        ShowMainWindowHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyShowMainWindow);
        ShowAllPinnedHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyShowAllPinned);
        HideAllPinnedHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyHideAllPinned);
        ShowUngroupedPinnedHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyShowUngroupedPinned);
        ShowPinnedGroupOneHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyShowPinnedGroupOne);
        ShowPinnedGroupTwoHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyShowPinnedGroupTwo);
        ShowPinnedGroupThreeHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyShowPinnedGroupThree);
        ExitApplicationHotkeyTextBlock.Text = FormatShortcut(settings.HotkeyExitApplication);
    }

    private static string FormatShortcut(string shortcut)
    {
        return string.IsNullOrWhiteSpace(shortcut) ? string.Empty : HotkeyTextFormatter.FormatText(shortcut);
    }

    private void ExecuteAndClose(Action action)
    {
        action();
        RequestClose();
    }

    private void RequestClose()
    {
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded)
            {
                return;
            }

            Close();
        }), System.Windows.Threading.DispatcherPriority.Background);
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

    private void ShowAllPinnedWindowsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_showAllPinnedWindowsAction);
    }

    private void HideAllPinnedWindowsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_hideAllPinnedWindowsAction);
    }

    private void ShowUngroupedPinnedWindowsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(_showUngroupedPinnedWindowsAction);
    }

    private void ShowPinnedGroupOneButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _showPinnedGroupAction(PinnedWindowRegistryService.GroupOneName));
    }

    private void ShowPinnedGroupTwoButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _showPinnedGroupAction(PinnedWindowRegistryService.GroupTwoName));
    }

    private void ShowPinnedGroupThreeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ExecuteAndClose(() => _showPinnedGroupAction(PinnedWindowRegistryService.GroupThreeName));
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_closeRequested)
        {
            return;
        }

        _closeRequested = true;
        Closed += ExitAfterTrayMenuClosed;
        Close();
    }

    private void ExitAfterTrayMenuClosed(object? sender, EventArgs e)
    {
        Closed -= ExitAfterTrayMenuClosed;
        Dispatcher.BeginInvoke(_exitAction, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
