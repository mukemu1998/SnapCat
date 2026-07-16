using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using CaptureWorkflowKind = SnapCat.Core.Models.CaptureWorkflowKind;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsMouseButtons = System.Windows.Forms.MouseButtons;
using FormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using MediaColor = System.Windows.Media.Color;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
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
    private Window? _trayHintWindow;
    private DispatcherTimer? _trayHintHideTimer;

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
        RefreshTrayNativeTooltipText();
        _notifyIcon.Visible = true;
        _notifyIcon.MouseUp -= NotifyIcon_OnMouseUp;
        _notifyIcon.MouseUp += NotifyIcon_OnMouseUp;
        _notifyIcon.MouseMove -= NotifyIcon_OnMouseMove;
        _notifyIcon.MouseMove += NotifyIcon_OnMouseMove;
    }

    public void Dispose()
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher?.CheckAccess() == true)
        {
            CloseTrayMenuWindow();
        }
        else
        {
            dispatcher?.BeginInvoke(CloseTrayMenuWindow);
        }

        _notifyIcon.Visible = false;
        CloseTrayHintWindow();
        _currentIcon?.Dispose();
        _currentIcon = null;
        _notifyIcon.Dispose();
    }

    private void CloseTrayMenuWindow()
    {
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.4.5-preview";
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
        WpfApplication.Current?.Dispatcher.BeginInvoke(CloseTrayHintWindow);
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

    private void NotifyIcon_OnMouseMove(object? sender, FormsMouseEventArgs e)
    {
        WpfApplication.Current?.Dispatcher.BeginInvoke(() =>
        {
            RefreshTrayNativeTooltipText();
            CloseTrayHintWindow();
        });
    }

    private void RefreshTrayNativeTooltipText()
    {
        try
        {
            var text = $"SnapCat v{GetAppVersion()}";
            if (_settingsGetter is not null && _trayLeftClickActionGetter is not null)
            {
                var settings = _settingsGetter();
                text = BuildNativeTrayTooltipText(settings, _trayLeftClickActionGetter());
            }

            _notifyIcon.Text = TrimNativeTrayTooltip(text);
        }
        catch
        {
            _notifyIcon.Text = "SnapCat";
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

    private void ShowTrayHintWindow()
    {
        if (_settingsGetter is null || _trayLeftClickActionGetter is null)
        {
            return;
        }

        var settings = _settingsGetter();
        var lines = BuildTrayHintLines(settings, _trayLeftClickActionGetter());
        if (_trayHintWindow is null)
        {
            _trayHintWindow = CreateTrayHintWindow(lines);
        }
        else
        {
            _trayHintWindow.Content = CreateTrayHintContent(lines);
        }

        var cursor = FormsCursor.Position;
        PositionTrayHintWindow(_trayHintWindow, cursor.X, cursor.Y);
        if (_trayHintWindow.IsVisible)
        {
            return;
        }

        _trayHintWindow.Show();
        PositionTrayHintWindow(_trayHintWindow, cursor.X, cursor.Y);
    }

    private static void PositionTrayHintWindow(Window window, int cursorX, int cursorY)
    {
        window.UpdateLayout();
        var width = Math.Max(220d, window.ActualWidth);
        var height = Math.Max(80d, window.ActualHeight);
        var workArea = SystemParameters.WorkArea;
        var left = cursorX + 12d;
        var top = cursorY - height - 10d;
        if (left + width > workArea.Right)
        {
            left = cursorX - width - 12d;
        }

        if (top < workArea.Top)
        {
            top = cursorY + 18d;
        }

        window.Left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        window.Top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
    }

    private void ResetTrayHintHideTimer()
    {
        _trayHintHideTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1800)
        };
        _trayHintHideTimer.Tick -= TrayHintHideTimer_OnTick;
        _trayHintHideTimer.Tick += TrayHintHideTimer_OnTick;
        _trayHintHideTimer.Stop();
        _trayHintHideTimer.Start();
    }

    private void TrayHintHideTimer_OnTick(object? sender, EventArgs e)
    {
        _trayHintHideTimer?.Stop();
        CloseTrayHintWindow();
    }

    private void CloseTrayHintWindow()
    {
        _trayHintHideTimer?.Stop();
        _trayHintWindow?.Close();
        _trayHintWindow = null;
    }

    private static Window CreateTrayHintWindow(IReadOnlyList<TrayHintLine> lines)
    {
        return new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = true,
            AllowsTransparency = true,
            ShowActivated = false,
            Background = WpfBrushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = CreateTrayHintContent(lines)
        };
    }

    private static Border CreateTrayHintContent(IReadOnlyList<TrayHintLine> lines)
    {
        var resources = WpfApplication.Current?.Resources;
        var accent = resources?["Theme.Brush.Accent"] as WpfBrush ?? WpfBrushes.DeepSkyBlue;
        var textPrimary = resources?["Theme.Brush.TextPrimary"] as WpfBrush ?? WpfBrushes.White;
        var textSecondary = resources?["Theme.Brush.TextSecondary"] as WpfBrush ?? WpfBrushes.LightGray;
        var background = resources?["Theme.Brush.WindowBackground"] as WpfBrush ?? new SolidColorBrush(MediaColor.FromArgb(242, 18, 24, 35));
        var borderBrush = resources?["Theme.Brush.WindowBorder"] as WpfBrush ?? WpfBrushes.DimGray;

        var stack = new StackPanel
        {
            MinWidth = 210
        };
        stack.Children.Add(new TextBlock
        {
            Text = "SnapCat",
            Foreground = accent,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        foreach (var line in lines)
        {
            var row = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, line.IsPrimary ? 0 : 3, 0, 0)
            };
            row.Children.Add(new TextBlock
            {
                Text = line.Label,
                Foreground = line.IsPrimary ? textPrimary : textSecondary,
                FontSize = line.IsPrimary ? 13 : 12,
                FontWeight = line.IsPrimary ? FontWeights.SemiBold : FontWeights.Normal
            });
            if (!string.IsNullOrWhiteSpace(line.Hotkey))
            {
                row.Children.Add(new TextBlock
                {
                    Text = $"  {line.Hotkey}",
                    Foreground = accent,
                    FontSize = 11,
                    Opacity = 0.88,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }

            stack.Children.Add(row);
        }

        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(14),
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Child = stack
        };
    }

    private static IReadOnlyList<TrayHintLine> BuildTrayHintLines(AppSettings settings, CaptureWorkflowKind leftClickAction)
    {
        var result = new List<TrayHintLine>
        {
            new($"单击托盘：{FormatWorkflowShortName(leftClickAction)}", FormatHotkey(GetWorkflowHotkey(settings, leftClickAction)), true)
        };

        foreach (var action in EnumerateTrayTooltipWorkflows(settings))
        {
            var hotkey = GetWorkflowHotkey(settings, action);
            if (string.IsNullOrWhiteSpace(hotkey))
            {
                continue;
            }

            result.Add(new TrayHintLine($"{FormatWorkflowShortName(action)}:", FormatHotkey(hotkey), false));
        }

        return result;
    }

    private static IEnumerable<CaptureWorkflowKind> EnumerateTrayTooltipWorkflows(AppSettings settings)
    {
        if (TryParseWorkflow(settings.TrayTooltipWorkflowOne, out var first))
        {
            yield return first;
        }

        if (TryParseWorkflow(settings.TrayTooltipWorkflowTwo, out var second))
        {
            yield return second;
        }
    }

    private static bool TryParseWorkflow(string value, out CaptureWorkflowKind workflow)
    {
        return Enum.TryParse(value, true, out workflow);
    }

    private static string BuildNativeTrayTooltipText(AppSettings settings, CaptureWorkflowKind leftClickAction)
    {
        const int nativeTooltipLimit = 63;
        var lines = new List<string> { $"SnapCat v{GetAppVersion()}" };
        foreach (var line in BuildTrayHintLines(settings, leftClickAction))
        {
            var nextLine = string.IsNullOrWhiteSpace(line.Hotkey)
                ? line.Label
                : $"{line.Label}{line.Hotkey}";
            var candidate = string.Join('\n', lines.Append(nextLine));
            if (candidate.Length > nativeTooltipLimit)
            {
                break;
            }

            lines.Add(nextLine);
        }

        return string.Join('\n', lines);
    }

    private static string TrimNativeTrayTooltip(string text)
    {
        const int nativeTooltipLimit = 63;
        return text.Length <= nativeTooltipLimit ? text : text[..nativeTooltipLimit].TrimEnd();
    }

    private static IEnumerable<(CaptureWorkflowKind Action, string Hotkey)> EnumerateWorkflowHotkeys(AppSettings settings)
    {
        yield return (CaptureWorkflowKind.CaptureAndTranslate, settings.HotkeyCaptureAndTranslate);
        yield return (CaptureWorkflowKind.CaptureAndPin, settings.HotkeyCaptureAndPin);
        yield return (CaptureWorkflowKind.CaptureAndWaitForAction, settings.HotkeyCaptureAndWaitForAction);
        yield return (CaptureWorkflowKind.CaptureAndOcr, settings.HotkeyCaptureAndOcr);
        yield return (CaptureWorkflowKind.CaptureAndSave, settings.HotkeyCaptureAndSave);
        yield return (CaptureWorkflowKind.CaptureAndCopy, settings.HotkeyCaptureAndCopy);
        yield return (CaptureWorkflowKind.CaptureAndAnnotate, settings.HotkeyCaptureAndAnnotate);
        yield return (CaptureWorkflowKind.FullScreenCanvasEdit, settings.HotkeyFullScreenCanvasEdit);
    }

    private static string GetWorkflowHotkey(AppSettings settings, CaptureWorkflowKind action)
    {
        return EnumerateWorkflowHotkeys(settings).FirstOrDefault(pair => pair.Action == action).Hotkey ?? string.Empty;
    }

    private static string FormatHotkey(string hotkey)
    {
        return HotkeyTextFormatter.FormatText(hotkey);
    }

    private static string FormatWorkflowShortName(CaptureWorkflowKind action)
    {
        return action switch
        {
            CaptureWorkflowKind.CaptureAndPin => "框选+贴图",
            CaptureWorkflowKind.CaptureAndOcr => "框选+OCR",
            CaptureWorkflowKind.CaptureAndTranslate => "框选+翻译",
            CaptureWorkflowKind.CaptureAndWaitForAction => "框选+待执行",
            CaptureWorkflowKind.CaptureAndSave => "框选+保存",
            CaptureWorkflowKind.CaptureAndCopy => "框选+复制",
            CaptureWorkflowKind.CaptureAndAnnotate => "框选+标注",
            CaptureWorkflowKind.FullScreenCanvasEdit => "全屏画布",
            _ => "框选"
        };
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

    private sealed record TrayHintLine(string Label, string Hotkey, bool IsPrimary);
}
