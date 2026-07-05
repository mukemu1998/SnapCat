using System.Diagnostics;
using System.IO;
using SnapCat.App.Services;
using SnapCat.Core.Models;

namespace SnapCat.App;

public partial class MainWindow
{
    private void InitializeTray()
    {
        _app.TrayIconService.Initialize(
            () => _settings,
            GetTrayLeftClickAction,
            workflow => _ = StartCaptureWorkflowAsync(workflow, returnToMainWindow: false),
            OpenSettingsFromTray,
            OpenHistoryFromTray,
            ShowMainWindow,
            OpenCaptureDirectoryFromTray,
            _app.PinnedWindowRegistryService.ShowAllWindows,
            _app.PinnedWindowRegistryService.HideAllWindows,
            _app.PinnedWindowRegistryService.ShowUngroupedWindows,
            _app.PinnedWindowRegistryService.ShowGroup,
            ExitApplication);
    }

    private CaptureWorkflowKind GetTrayLeftClickAction()
    {
        return Enum.TryParse<CaptureWorkflowKind>(_settings.TrayLeftClickAction, out var action)
            ? action
            : CaptureWorkflowKind.CaptureAndWaitForAction;
    }

    private void OpenSettingsFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ShowMainWindow();
            SelectSection(MainSection.OcrSettings);
            StatusTextBlock.Text = "已打开主窗口设置页。";
        });
    }

    private void OpenHistoryFromTray()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            ShowMainWindow();
            SelectSection(MainSection.History);
            await LoadHistoryAsync();

            if (HistoryListBox.Items.Count > 0 && HistoryListBox.SelectedIndex < 0)
            {
                HistoryListBox.SelectedIndex = 0;
            }

            HistoryListBox.Focus();
            StatusTextBlock.Text = "已打开历史记录。";
        });
    }

    private void ChangeTrayLeftClickActionFromTray(CaptureWorkflowKind action)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                _settings.TrayLeftClickAction = action.ToString();
                await _app.SettingsStore.SaveAsync(_settings);
                ApplySettingsToControls(_settings);
                RenderSettingsSummary();
                StatusTextBlock.Text = $"托盘左键默认动作已切换为：{SettingsSummaryFormatter.FormatTrayLeftClickAction(_settings.TrayLeftClickAction)}";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"切换托盘左键默认动作失败：{ex.Message}";
            }
        });
    }

    private void OpenCaptureDirectoryFromTray()
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var directory = _app.CapturedImageFileService.GetDefaultDirectoryPath();
                WindowsExplorerService.OpenDirectory(directory, createIfMissing: true);

                StatusTextBlock.Text = "已打开 SnapCat 默认截图目录。";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"打开截图目录失败：{ex.Message}";
            }
        });
    }
}
