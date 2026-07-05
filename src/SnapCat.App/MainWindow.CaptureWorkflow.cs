using System.Windows;
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
        CaptureButton.IsEnabled = false;
        StatusTextBlock.Text = "请在屏幕上进行自由框选。";

        Window? owner = null;
        var wasMinimizedToTaskbar = IsVisible
            && WindowState == WindowState.Minimized
            && ShowInTaskbar;
        var shouldRestoreMainWindow = returnToMainWindow && hideMainWindowForCapture;

        try
        {
            if (hideMainWindowForCapture && !wasMinimizedToTaskbar)
            {
                HideMainWindow();
                await Task.Delay(180);
            }

            var overlay = new SelectionOverlayWindow();
            var selectionConfirmed = overlay.ShowDialog() == true && overlay.SelectedRegion is not null;

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
                case CaptureWorkflowKind.CaptureAndTranslate:
                    action = CaptureActionKind.OcrAndTranslate;
                    break;
                case CaptureWorkflowKind.CaptureAndSave:
                    action = CaptureActionKind.Save;
                    break;
                default:
                {
                    var selection = await SelectActionAsync(captureRegion);
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

            await Task.Delay(120);
            var workingImagePath = _app.ScreenCaptureService.CaptureToTempFile(workingCaptureRegion);
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

            if (action is CaptureActionKind.PinToScreen
                or CaptureActionKind.OcrOnly
                or CaptureActionKind.OcrAndTranslate
                or CaptureActionKind.QrCode)
            {
                workingImagePath = _app.CapturedImageFileService.SaveToDefaultDirectory(workingImagePath);
            }

            var status = await _app.CaptureActionService.ExecuteAsync(
                action,
                workingImagePath,
                CloneSettings(_settings),
                owner,
                workingCaptureRegion,
                action == CaptureActionKind.OcrAndTranslate
                    ? () => StartCaptureWorkflowAsync(CaptureWorkflowKind.CaptureAndTranslate, returnToMainWindow: false)
                    : null);

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
            if (wasMinimizedToTaskbar && !returnToMainWindow && !_isExitRequested)
            {
                KeepMainWindowMinimizedInTaskbar();
            }

            CaptureButton.IsEnabled = true;
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

    private async Task<CaptureActionSelectionResult> SelectActionAsync(Int32Rect captureRegion)
    {
        var actionWindow = new CaptureActionSelectionWindow(captureRegion);

        return await Task.FromResult(actionWindow.ShowDialog() == true
            ? new CaptureActionSelectionResult(
                actionWindow.SelectedAction,
                actionWindow.SelectedRegion)
            : new CaptureActionSelectionResult(
                CaptureActionKind.Cancel,
                captureRegion));
    }

    private sealed record CaptureActionSelectionResult(
        CaptureActionKind Action,
        Int32Rect CaptureRegion);
}
