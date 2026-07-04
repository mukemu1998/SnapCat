using System.Drawing;
using System.IO;
using SnapCat.App.Windows;
using CaptureWorkflowKind = SnapCat.Core.Models.CaptureWorkflowKind;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly FormsNotifyIcon _notifyIcon = new();
    private Func<CaptureWorkflowKind>? _trayLeftClickActionGetter;
    private Action<CaptureWorkflowKind>? _startCaptureAction;
    private Action<CaptureWorkflowKind>? _setTrayLeftClickAction;
    private Action? _openSettingsAction;
    private Action? _openHistoryAction;
    private Action? _showMainWindowAction;
    private Action? _openCaptureDirectoryAction;
    private Action? _exitAction;
    private TrayMenuWindow? _trayMenuWindow;

    public void Initialize(
        Func<CaptureWorkflowKind> trayLeftClickActionGetter,
        Action<CaptureWorkflowKind> startCaptureAction,
        Action<CaptureWorkflowKind> setTrayLeftClickAction,
        Action openSettingsAction,
        Action openHistoryAction,
        Action showMainWindowAction,
        Action openCaptureDirectoryAction,
        Action exitAction)
    {
        _trayLeftClickActionGetter = trayLeftClickActionGetter;
        _startCaptureAction = startCaptureAction;
        _setTrayLeftClickAction = setTrayLeftClickAction;
        _openSettingsAction = openSettingsAction;
        _openHistoryAction = openHistoryAction;
        _showMainWindowAction = showMainWindowAction;
        _openCaptureDirectoryAction = openCaptureDirectoryAction;
        _exitAction = exitAction;

        _notifyIcon.Icon = CreateAppIcon();
        _notifyIcon.Text = "SnapCat v0.1.0";
        _notifyIcon.Visible = true;
        _notifyIcon.MouseUp -= NotifyIcon_OnMouseUp;
        _notifyIcon.MouseUp += NotifyIcon_OnMouseUp;
    }

    public void Dispose()
    {
        WpfApplication.Current?.Dispatcher.Invoke(() =>
        {
            _trayMenuWindow?.Close();
            _trayMenuWindow = null;
        });

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    private void NotifyIcon_OnMouseUp(object? sender, FormsMouseEventArgs e)
    {
        if (_trayLeftClickActionGetter is null || _startCaptureAction is null)
        {
            return;
        }

        if (e.Button == FormsMouseButtons.Left)
        {
            _startCaptureAction(_trayLeftClickActionGetter());
            return;
        }

        if (e.Button == FormsMouseButtons.Right)
        {
            WpfApplication.Current?.Dispatcher.BeginInvoke(ShowOrToggleTrayMenu);
        }
    }

    private void ShowOrToggleTrayMenu()
    {
        if (_trayLeftClickActionGetter is null
            || _startCaptureAction is null
            || _setTrayLeftClickAction is null
            || _openSettingsAction is null
            || _openHistoryAction is null
            || _showMainWindowAction is null
            || _openCaptureDirectoryAction is null
            || _exitAction is null)
        {
            return;
        }

        if (_trayMenuWindow?.IsVisible == true)
        {
            _trayMenuWindow.Close();
            _trayMenuWindow = null;
            return;
        }

        var menuWindow = new TrayMenuWindow(
            _trayLeftClickActionGetter(),
            _startCaptureAction,
            _setTrayLeftClickAction,
            _showMainWindowAction,
            _openHistoryAction,
            _openSettingsAction,
            _openCaptureDirectoryAction,
            _exitAction);

        menuWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_trayMenuWindow, menuWindow))
            {
                _trayMenuWindow = null;
            }
        };

        _trayMenuWindow = menuWindow;
        menuWindow.ShowAt(FormsCursor.Position);
    }

    private static Icon CreateAppIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            using var icon = Icon.ExtractAssociatedIcon(processPath);
            if (icon is not null)
            {
                return (Icon)icon.Clone();
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
