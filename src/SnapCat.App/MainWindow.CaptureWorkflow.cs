using System.Windows;
using SnapCat.App.Services;
using SnapCat.App.Windows;
using SnapCat.Core.Models;

namespace SnapCat.App;

public partial class MainWindow
{
    private async Task StartCaptureWorkflowAsync(
        CaptureWorkflowKind workflow,
        bool returnToMainWindow,
        bool hideMainWindowForCapture = true)
    {
        if (_isCaptureWorkflowActive)
        {
            StatusTextBlock.Text = "自由框选已经在进行中。";
            return;
        }

        _isCaptureWorkflowActive = true;
        CaptureButton.IsEnabled = false;
        StatusTextBlock.Text = "请在屏幕上进行自由框选。";

        Window? owner = null;
        var wasMinimizedToTaskbar = IsVisible
            && WindowState == WindowState.Minimized
            && ShowInTaskbar;
        var shouldRestoreMainWindow = returnToMainWindow && hideMainWindowForCapture;

        SelectionOverlayWindow? overlay = null;

        try
        {
            if (hideMainWindowForCapture && !wasMinimizedToTaskbar)
            {
                HideMainWindow();
                await Task.Delay(180);
            }

            var shouldUseImmediateSnapshot = CaptureStartupMode.Normalize(_settings.CaptureStartupMode) == CaptureStartupMode.Snapshot;
            var screenSnapshotRegion = shouldUseImmediateSnapshot
                ? _app.ScreenCaptureService.GetCurrentScreenRegion()
                : (Int32Rect?)null;
            var screenSnapshotPath = shouldUseImmediateSnapshot
                ? _app.ScreenCaptureService.CaptureCurrentScreenToTempFile()
                : null;

            overlay = new SelectionOverlayWindow(screenSnapshotRegion, screenSnapshotPath);
            var selectionConfirmed = shouldUseImmediateSnapshot
                ? await overlay.ShowForSelectionAsync(keepVisibleAfterSelection: true) && overlay.SelectedRegion is not null
                : overlay.ShowDialog() == true && overlay.SelectedRegion is not null;

            if (!selectionConfirmed)
            {
                if (shouldRestoreMainWindow)
                {
                    ShowMainWindow();
                }

                StatusTextBlock.Text = "已取消自由框选。";
                return;
            }

            var captureRegion = overlay.SelectedRegion!.Value;
            var action = CaptureActionKind.Cancel;
            var workingCaptureRegion = captureRegion;

            switch (workflow)
            {
                case CaptureWorkflowKind.CaptureAndPin:
                    action = CaptureActionKind.PinToScreen;
                    break;
                case CaptureWorkflowKind.CaptureAndOcr:
                    action = CaptureActionKind.OcrOnly;
                    break;
                case CaptureWorkflowKind.CaptureAndTranslate:
                    action = CaptureActionKind.OcrAndTranslate;
                    break;
                case CaptureWorkflowKind.CaptureAndSave:
                    action = CaptureActionKind.Save;
                    break;
                default:
                {
                    var selection = await SelectActionAsync(captureRegion, shouldUseImmediateSnapshot);
                    action = selection.Action;
                    workingCaptureRegion = selection.CaptureRegion;
                    break;
                }
            }

            if (action == CaptureActionKind.Cancel)
            {
                if (shouldRestoreMainWindow)
                {
                    ShowMainWindow();
                }

                StatusTextBlock.Text = "已取消截图后操作。";
                return;
            }

            if (IsWindowsTextRecognitionEngine(_settings.OcrEngine)
                && (action == CaptureActionKind.OcrOnly || action == CaptureActionKind.OcrAndTranslate))
            {
                var keepSnapshotOverlayForWindowsText = shouldUseImmediateSnapshot && overlay is not null && overlay.IsVisible;
                if (!keepSnapshotOverlayForWindowsText && overlay is not null && overlay.IsVisible)
                {
                    overlay.Close();
                    overlay = null;
                    await Task.Delay(160);
                }

                await StartWindowsTextRecognitionBridgeAsync(
                    action == CaptureActionKind.OcrAndTranslate,
                    workingCaptureRegion);
                var windowsTextStatusPrefix = keepSnapshotOverlayForWindowsText
                    ? "已基于临时定屏触发 Win+Shift+T"
                    : "已触发 Win+Shift+T";
                StatusTextBlock.Text = action == CaptureActionKind.OcrAndTranslate
                    ? $"{windowsTextStatusPrefix}，并尝试用当前选框自动提取后翻译。"
                    : $"{windowsTextStatusPrefix}，并尝试用当前选框自动提取。";

                if (keepSnapshotOverlayForWindowsText && overlay is not null && overlay.IsVisible)
                {
                    overlay.Close();
                    overlay = null;
                }

                if (shouldRestoreMainWindow)
                {
                    ShowMainWindow();
                }

                return;
            }

            await Task.Delay(120);
            var workingImagePath = !string.IsNullOrWhiteSpace(screenSnapshotPath) && screenSnapshotRegion is not null
                ? _app.ScreenCaptureService.CropSnapshotToTempFile(
                    screenSnapshotPath,
                    screenSnapshotRegion.Value,
                    workingCaptureRegion)
                : _app.ScreenCaptureService.CaptureToTempFile(workingCaptureRegion);
            if (action == CaptureActionKind.Save)
            {
                var savedPath = await SaveCaptureToDefaultDirectoryAsync(workingImagePath);
                StatusTextBlock.Text = $"截图已保存到默认目录：{savedPath}";
                await LoadHistoryAsync();

                if (shouldRestoreMainWindow)
                {
                    ShowMainWindow();
                }

                return;
            }

            if (action == CaptureActionKind.PinToScreen)
            {
                workingImagePath = _app.CapturedImageFileService.SaveToPinnedCacheDirectory(workingImagePath);
            }

            var status = await _app.CaptureActionService.ExecuteAsync(
                action,
                workingImagePath,
                TranslationLanguageHelper.CloneSettings(_settings),
                owner,
                workingCaptureRegion,
                action == CaptureActionKind.OcrAndTranslate
                    ? () => StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: false)
                    : null,
                screenSnapshotPath,
                screenSnapshotRegion,
                reuseExistingSelectionChrome: false);

            StatusTextBlock.Text = status;
            await LoadHistoryAsync();

            if (shouldRestoreMainWindow)
            {
                ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            if (shouldRestoreMainWindow)
            {
                ShowMainWindow();
            }

            StatusTextBlock.Text = $"执行失败：{ex.Message}";
        }
        finally
        {
            if (overlay is not null && overlay.IsVisible)
            {
                overlay.Close();
            }

            if (wasMinimizedToTaskbar && !returnToMainWindow && !_isExitRequested)
            {
                KeepMainWindowMinimizedInTaskbar();
            }

            CaptureButton.IsEnabled = true;
            _isCaptureWorkflowActive = false;
        }
    }

    private async Task<string> SaveCaptureToDefaultDirectoryAsync(string imagePath)
    {
        var savedPath = _app.CapturedImageFileService.SaveToDefaultDirectory(imagePath);
        await _app.HistoryStore.AppendAsync(new CaptureTranslationRecord
        {
            WorkflowType = "save",
            ImagePath = savedPath
        });

        return savedPath;
    }

    private async Task<CaptureActionSelectionResult> SelectActionAsync(Int32Rect captureRegion, bool keepVisibleForOcrActions)
    {
        var actionWindow = new CaptureActionSelectionWindow(
            captureRegion,
            IsWindowsTextRecognitionEngine(_settings.OcrEngine));

        var confirmed = keepVisibleForOcrActions
            ? await actionWindow.ShowForActionSelectionAsync(keepVisibleForOcrActions: true)
            : actionWindow.ShowDialog() == true;

        return confirmed
            ? new CaptureActionSelectionResult(
                actionWindow.SelectedAction,
                actionWindow.SelectedRegion)
            : new CaptureActionSelectionResult(
                CaptureActionKind.Cancel,
                captureRegion);
    }

    private sealed record CaptureActionSelectionResult(
        CaptureActionKind Action,
        Int32Rect CaptureRegion);

    private static bool IsWindowsTextRecognitionEngine(string? value)
    {
        return string.Equals(value, "windows-text-extractor", StringComparison.Ordinal)
            || string.Equals(value, "windows-snipping-clipboard", StringComparison.Ordinal);
    }
}
