using System.Drawing;
using System.IO;
using System.Reflection;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using CaptureWorkflowKind = SnapCat.Core.Models.CaptureWorkflowKind;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using MediaColor = System.Windows.Media.Color;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly FormsNotifyIcon _notifyIcon = new();
    private Icon? _currentIcon;
    private Func<AppSettings>? _settingsGetter;
    private Func<CaptureWorkflowKind>? _trayLeftClickActionGetter;
    private Action<CaptureWorkflowKind>? _startCaptureAction;
    private Action? _openSettingsAction;
    private Action? _openHistoryAction;
    private Action? _showMainWindowAction;
    private Action? _openCaptureDirectoryAction;
    private Action? _showAllPinnedWindowsAction;
    private Action? _hideAllPinnedWindowsAction;
    private Action? _showUngroupedPinnedWindowsAction;
    private Action<string>? _showPinnedGroupAction;
    private Action? _exitAction;
    private TrayMenuWindow? _trayMenuWindow;

    public void Initialize(
        Func<AppSettings> settingsGetter,
        Func<CaptureWorkflowKind> trayLeftClickActionGetter,
        Action<CaptureWorkflowKind> startCaptureAction,
        Action openSettingsAction,
        Action openHistoryAction,
        Action showMainWindowAction,
        Action openCaptureDirectoryAction,
        Action showAllPinnedWindowsAction,
        Action hideAllPinnedWindowsAction,
        Action showUngroupedPinnedWindowsAction,
        Action<string> showPinnedGroupAction,
        Action exitAction)
    {
        _settingsGetter = settingsGetter;
        _trayLeftClickActionGetter = trayLeftClickActionGetter;
        _startCaptureAction = startCaptureAction;
        _openSettingsAction = openSettingsAction;
        _openHistoryAction = openHistoryAction;
        _showMainWindowAction = showMainWindowAction;
        _openCaptureDirectoryAction = openCaptureDirectoryAction;
        _showAllPinnedWindowsAction = showAllPinnedWindowsAction;
        _hideAllPinnedWindowsAction = hideAllPinnedWindowsAction;
        _showUngroupedPinnedWindowsAction = showUngroupedPinnedWindowsAction;
        _showPinnedGroupAction = showPinnedGroupAction;
        _exitAction = exitAction;

        RefreshThemeIcon();
        _notifyIcon.Text = $"SnapCat v{GetAppVersion()}";
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
        _currentIcon?.Dispose();
        _currentIcon = null;
        _notifyIcon.Dispose();
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.3.2-preview";
        return version.Split('+', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? version;
    }

    public void RefreshThemeIcon()
    {
        var nextIcon = CreateThemeIconOrFallback();
        var previousIcon = _currentIcon;
        _notifyIcon.Icon = nextIcon;
        _currentIcon = nextIcon;
        previousIcon?.Dispose();
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
        if (_settingsGetter is null
            || _trayLeftClickActionGetter is null
            || _startCaptureAction is null
            || _openSettingsAction is null
            || _openHistoryAction is null
            || _showMainWindowAction is null
            || _openCaptureDirectoryAction is null
            || _showAllPinnedWindowsAction is null
            || _hideAllPinnedWindowsAction is null
            || _showUngroupedPinnedWindowsAction is null
            || _showPinnedGroupAction is null
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
            _settingsGetter(),
            _showMainWindowAction,
            _openHistoryAction,
            _openSettingsAction,
            _openCaptureDirectoryAction,
            _showAllPinnedWindowsAction,
            _hideAllPinnedWindowsAction,
            _showUngroupedPinnedWindowsAction,
            _showPinnedGroupAction,
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

    private static Icon CreateThemeIconOrFallback()
    {
        try
        {
            var resources = WpfApplication.Current?.Resources;
            if (resources?["Theme.Color.Accent"] is MediaColor accent
                && resources["Theme.Color.HighlightAlt"] is MediaColor highlight)
            {
                return ThemedLogoService.CreateTrayIcon(accent, highlight);
            }
        }
        catch
        {
            // 图标主题化失败时回退到固定 exe 图标，避免影响托盘可用性。
        }

        return CreateAppIcon();
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
