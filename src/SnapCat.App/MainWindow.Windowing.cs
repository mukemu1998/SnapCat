using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SnapCat.App.Services;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App;

public partial class MainWindow
{
    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideMainWindow();
    }

    private void ShowMainWindow()
    {
        ShowInTaskbar = true;

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        CenterOnPrimaryScreen();

        if (!IsVisible)
        {
            Show();
        }

        UpdateMaximizeRestoreButtonText();
        Activate();
        Focus();
        Topmost = true;
        Topmost = false;
    }

    private void HideMainWindow()
    {
        ShowInTaskbar = false;
        Hide();
    }

    private void KeepMainWindowMinimizedInTaskbar()
    {
        ShowInTaskbar = true;

        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Minimized;
        UpdateMaximizeRestoreButtonText();
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        _app.PreparePinnedWindowsForExit();
        Close();
        WpfApplication.Current.Shutdown();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        SavePendingSettingsBeforeDismiss();

        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideMainWindow();
    }

    private void SavePendingSettingsBeforeDismiss()
    {
        if (!_hasLoadedSettings || _isApplyingSettings || !_hasUnsavedSettings)
        {
            return;
        }

        var currentSettings = BuildCurrentSettings();
        if (SettingsComparer.AreEquivalent(currentSettings, _settings))
        {
            MarkSettingsClean();
            return;
        }

        try
        {
            currentSettings.NormalizeApiProfiles();
            _app.SettingsStore.SaveAsync(currentSettings).ConfigureAwait(false).GetAwaiter().GetResult();
            _settings = currentSettings;
            MarkSettingsClean();
            StatusTextBlock.Text = "设置已自动保存。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"自动保存设置失败：{ex.Message}";
        }
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        UpdateMaximizeRestoreButtonText();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

        UpdateMaximizeRestoreButtonText();
    }

    private void UpdateMaximizeRestoreButtonText()
    {
        if (MaximizeRestoreIconPath is null)
        {
            return;
        }

        MaximizeRestoreIconPath.Data = Geometry.Parse(WindowState == WindowState.Maximized
            ? "M8 10 V7 H17 V16 H14 M7 10 H14 V17 H7 Z"
            : "M7 7 H17 V17 H7 Z");
    }

    private void CenterOnPrimaryScreen()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
    }
}
