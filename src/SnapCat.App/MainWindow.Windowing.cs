using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.Core.Services;
using DrawingBitmap = System.Drawing.Bitmap;
using WpfApplication = System.Windows.Application;

namespace SnapCat.App;

public partial class MainWindow
{
    private const int WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;

    private IntPtr _themeSmallIconHandle;
    private IntPtr _themeLargeIconHandle;

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
        RefreshWindowThemeIcon();
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
            // The OS reclaims the final themed HICON handles on process exit.
            // Destroying them while the closing HWND still owns them can stall shutdown.
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
        if (AppSettingsComparer.AreEquivalent(currentSettings, _settings))
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

    private void ApplyWindowThemeIcon()
    {
        if (_isExitRequested)
        {
            return;
        }

        if (WpfApplication.Current.Resources["Theme.Image.AppLogo"] is not ImageSource logo)
        {
            return;
        }

        // Reassigning refreshes WPF chrome; WM_SETICON below refreshes the native taskbar icons.
        Icon = null;
        Icon = logo;
        ApplyNativeWindowThemeIcon(logo);
    }

    private void RefreshWindowThemeIcon()
    {
        if (_isExitRequested)
        {
            return;
        }

        ApplyWindowThemeIcon();
        _ = Dispatcher.InvokeAsync(ApplyWindowThemeIcon, DispatcherPriority.ApplicationIdle);
        _ = Task.Run(async () =>
        {
            await Task.Delay(250).ConfigureAwait(false);
            if (!_isExitRequested)
            {
                await Dispatcher.InvokeAsync(ApplyWindowThemeIcon, DispatcherPriority.ApplicationIdle);
            }
        });
    }

    private void ApplyNativeWindowThemeIcon(ImageSource logo)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var smallIcon = CreateIconHandle(logo, GetSystemMetrics(SystemMetric.SmallIconWidth), GetSystemMetrics(SystemMetric.SmallIconHeight));
        var largeIcon = CreateIconHandle(logo, GetSystemMetrics(SystemMetric.IconWidth), GetSystemMetrics(SystemMetric.IconHeight));
        var previousSmallIcon = _themeSmallIconHandle;
        var previousLargeIcon = _themeLargeIconHandle;

        _themeSmallIconHandle = smallIcon;
        _themeLargeIconHandle = largeIcon;

        SendMessage(hwnd, WmSetIcon, IconSmall, smallIcon);
        SendMessage(hwnd, WmSetIcon, IconBig, largeIcon);

        DestroyIconHandle(previousSmallIcon);
        DestroyIconHandle(previousLargeIcon);
    }

    private static IntPtr CreateIconHandle(ImageSource source, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var bitmapSource = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmapSource.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        using var bitmap = new DrawingBitmap(stream);
        return bitmap.GetHicon();
    }

    private static void DestroyIconHandle(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            DestroyIcon(handle);
        }
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

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static class SystemMetric
    {
        public const int IconWidth = 11;
        public const int IconHeight = 12;
        public const int SmallIconWidth = 49;
        public const int SmallIconHeight = 50;
    }
}
