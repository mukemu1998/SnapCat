using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using Clipboard = System.Windows.Clipboard;
using WpfMessageBox = System.Windows.MessageBox;

namespace SnapCat.App.Services;

public sealed class CaptureActionService
{
    private readonly IOcrService _ocrService;
    private readonly ITranslationService _translationService;
    private readonly IQrCodeService _qrCodeService;
    private readonly IHistoryStore _historyStore;
    private readonly CapturedImageFileService _fileService;
    private readonly ScreenCaptureService _screenCaptureService;
    private TranslationPopupWindow? _translationPopupWindow;
    private readonly List<OcrSelectionOverlayWindow> _ocrOverlayWindows = [];
    private static readonly TimeSpan QrCodeDecodeTimeout = TimeSpan.FromSeconds(8);

    public CaptureActionService(
        IOcrService ocrService,
        ITranslationService translationService,
        IQrCodeService qrCodeService,
        IHistoryStore historyStore,
        CapturedImageFileService fileService,
        ScreenCaptureService screenCaptureService)
    {
        _ocrService = ocrService;
        _translationService = translationService;
        _qrCodeService = qrCodeService;
        _historyStore = historyStore;
        _fileService = fileService;
        _screenCaptureService = screenCaptureService;
    }

    public async Task<string> ExecuteAsync(
        CaptureActionKind action,
        string imagePath,
        AppSettings settings,
        Window? owner = null,
        Int32Rect? captureRegion = null,
        Func<Task>? repeatCaptureAction = null,
        string? screenSnapshotPath = null,
        Int32Rect? screenSnapshotRegion = null,
        bool reuseExistingSelectionChrome = false,
        Window? retainedSelectionChromeWindow = null)
    {
        return action switch
        {
            CaptureActionKind.PinToScreen => ExecutePinToScreen(imagePath, settings, captureRegion),
            CaptureActionKind.OcrOnly => await ExecuteOcrOnlyAsync(imagePath, settings, owner, captureRegion, screenSnapshotPath, screenSnapshotRegion, reuseExistingSelectionChrome, retainedSelectionChromeWindow),
            CaptureActionKind.OcrAndTranslate => await ExecuteOcrAndTranslateAsync(imagePath, settings, owner, captureRegion, repeatCaptureAction, screenSnapshotPath, screenSnapshotRegion, reuseExistingSelectionChrome, retainedSelectionChromeWindow),
            CaptureActionKind.QrCode => await ExecuteQrCodeAsync(imagePath, owner, captureRegion),
            CaptureActionKind.CopyImage => ExecuteCopyImage(imagePath),
            CaptureActionKind.Save => ExecuteSave(imagePath, owner),
            CaptureActionKind.SaveAs => ExecuteSaveAs(imagePath, owner),
            _ => "已取消操作。"
        };
    }

    public bool TemporarilyHideActiveTranslationPopup()
    {
        if (_translationPopupWindow is null || !_translationPopupWindow.IsVisible)
        {
            return false;
        }

        _translationPopupWindow.Hide();
        return true;
    }

    public void RestoreActiveTranslationPopup()
    {
        if (_translationPopupWindow is null || _translationPopupWindow.IsVisible)
        {
            return;
        }

        _translationPopupWindow.ShowAboveSelectionOverlay();
    }

    private string ExecutePinToScreen(string imagePath, AppSettings settings, Int32Rect? captureRegion)
    {
        var pinnedWindow = new PinnedImageWindow(
            imagePath,
            TranslationLanguageHelper.CloneSettings(settings),
            captureRegion);
        pinnedWindow.Show();

        _historyStore.AppendAsync(new CaptureTranslationRecord
        {
            WorkflowType = "pin",
            ImagePath = imagePath
        }).ConfigureAwait(false);

        return "截图已固定到屏幕。";
    }

    private async Task<string> ExecuteOcrOnlyAsync(
        string imagePath,
        AppSettings settings,
        Window? owner,
        Int32Rect? captureRegion,
        string? screenSnapshotPath,
        Int32Rect? screenSnapshotRegion,
        bool reuseExistingSelectionChrome,
        Window? retainedSelectionChromeWindow)
    {
        var overlayWindow = CreateOcrOverlay(
            captureRegion,
            settings,
            onFirstRecognition: async result =>
            {
                await _historyStore.AppendAsync(new CaptureTranslationRecord
                {
                    WorkflowType = "ocr",
                    ImagePath = imagePath,
                    SourceText = result.Text,
                    OcrError = result.ErrorMessage,
                    OcrDebugInfo = result.DebugSummary
                });
            },
            screenSnapshotPath: screenSnapshotPath,
            screenSnapshotRegion: screenSnapshotRegion,
            showSelectionChrome: !reuseExistingSelectionChrome,
            retainedSelectionChromeWindow: retainedSelectionChromeWindow);
        if (overlayWindow is not null)
        {
            overlayWindow.Show();
            return "OCR 识别层已打开。";
        }

        CloseRetainedSelectionChrome(retainedSelectionChromeWindow);

        var ocrResult = await _ocrService.RecognizeAsync(imagePath, settings);
        await _historyStore.AppendAsync(new CaptureTranslationRecord
        {
            WorkflowType = "ocr",
            ImagePath = imagePath,
            SourceText = ocrResult.Text,
            OcrError = ocrResult.ErrorMessage,
            OcrDebugInfo = ocrResult.DebugSummary
        });

        var status = ocrResult.Success ? "OCR 文本识别已完成。" : $"OCR 识别失败：{ocrResult.ErrorMessage}";
        var resultWindow = new ResultWindow(
            "OCR 文本识别结果",
            status,
            "OCR 文本",
            ocrResult.Success ? ocrResult.Text : ocrResult.ErrorMessage,
            "截图路径",
            imagePath,
            ocrResult.DebugSummary,
            imagePath: imagePath)
        {
            Owner = owner
        };
        resultWindow.ShowDialog();

        return status;
    }

    private async Task<string> ExecuteOcrAndTranslateAsync(
        string imagePath,
        AppSettings settings,
        Window? owner,
        Int32Rect? captureRegion,
        Func<Task>? repeatCaptureAction,
        string? screenSnapshotPath,
        Int32Rect? screenSnapshotRegion,
        bool reuseExistingSelectionChrome,
        Window? retainedSelectionChromeWindow)
    {
        var isReusingPopup = _translationPopupWindow is not null;
        var popupWindow = GetOrCreateTranslationPopupWindow(owner);
        Func<Task>? effectiveRepeatCaptureAction = async () =>
        {
            var activeOverlay = GetActiveOcrOverlay();
            if (activeOverlay is not null)
            {
                activeOverlay.BringToFrontForAdjustment();
                popupWindow.ShowAboveSelectionOverlay();
                return;
            }

            if (repeatCaptureAction is not null)
            {
                await repeatCaptureAction();
            }
        };
        popupWindow.PrepareForReuse(
            "OCR 识别并翻译",
            TranslationLanguageHelper.CloneSettings(settings),
            captureRegion,
            effectiveRepeatCaptureAction,
            preserveCurrentPosition: isReusingPopup);
        popupWindow.SetBusyState("正在识别文本...");

        var overlayWindow = CreateOcrOverlay(
            captureRegion,
            settings,
            onRecognitionCompleted: async (result, cancellationToken) =>
            {
                await UpdateTranslationPopupFromOcrAsync(
                    result,
                    imagePath,
                    popupWindow,
                    cancellationToken);
            },
            screenSnapshotPath: screenSnapshotPath,
            screenSnapshotRegion: screenSnapshotRegion,
            showSelectionChrome: !reuseExistingSelectionChrome,
            retainedSelectionChromeWindow: retainedSelectionChromeWindow);
        if (overlayWindow is not null)
        {
            overlayWindow.Show();
        }
        else
        {
            CloseRetainedSelectionChrome(retainedSelectionChromeWindow);
        }

        popupWindow.ShowAboveSelectionOverlay();

        if (overlayWindow is not null)
        {
            return "OCR 识别并翻译层已打开。";
        }

        var ocrResult = await _ocrService.RecognizeAsync(imagePath, settings);
        return await UpdateTranslationPopupFromOcrAsync(
            ocrResult,
            imagePath,
            popupWindow,
            CancellationToken.None);
    }

    private OcrSelectionOverlayWindow? CreateOcrOverlay(
        Int32Rect? captureRegion,
        AppSettings settings,
        Func<OcrResult, Task>? onFirstRecognition = null,
        Func<OcrResult, CancellationToken, Task>? onRecognitionCompleted = null,
        string? screenSnapshotPath = null,
        Int32Rect? screenSnapshotRegion = null,
        bool showSelectionChrome = true,
        Window? retainedSelectionChromeWindow = null)
    {
        if (captureRegion is not { Width: > 0, Height: > 0 } region)
        {
            return null;
        }

        var virtualScreenRegion = screenSnapshotRegion ?? _screenCaptureService.GetVirtualScreenRegion();
        var effectiveSnapshotPath = !string.IsNullOrWhiteSpace(screenSnapshotPath)
            ? screenSnapshotPath
            : _screenCaptureService.CaptureVirtualScreenToTempFile();
        var firstRecognitionRecorded = false;
        var overlayWindow = new OcrSelectionOverlayWindow(
            region,
            virtualScreenRegion,
            effectiveSnapshotPath,
            async (selectedRegion, cancellationToken) =>
            {
                var cropPath = _screenCaptureService.CropSnapshotToTempFile(
                    effectiveSnapshotPath,
                    virtualScreenRegion,
                    selectedRegion);
                var result = await _ocrService.RecognizeAsync(cropPath, settings, cancellationToken);

                if (!firstRecognitionRecorded && onFirstRecognition is not null)
                {
                    firstRecognitionRecorded = true;
                    await onFirstRecognition(result);
                }

                return result;
            },
            showSelectionChrome,
            onRecognitionCompleted);
        overlayWindow.Closed += (_, _) =>
        {
            _ocrOverlayWindows.Remove(overlayWindow);
            CloseRetainedSelectionChrome(retainedSelectionChromeWindow);
        };
        _ocrOverlayWindows.Add(overlayWindow);
        return overlayWindow;
    }

    private OcrSelectionOverlayWindow? GetActiveOcrOverlay()
    {
        return _ocrOverlayWindows.LastOrDefault(window => window.IsVisible);
    }

    private async Task<string> UpdateTranslationPopupFromOcrAsync(
        OcrResult ocrResult,
        string imagePath,
        TranslationPopupWindow popupWindow,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return "OCR 识别已取消。";
        }

        popupWindow.UpdateRecognizedSource(
            ocrResult.Success ? ocrResult.Text : ocrResult.ErrorMessage,
            ocrResult.Success ? "正在翻译..." : $"OCR 识别失败：{ocrResult.ErrorMessage}");
        popupWindow.ShowAboveSelectionOverlay();

        var popupSettings = popupWindow.CreateCurrentSettingsSnapshot();
        var effectiveSettings = TranslationLanguageHelper.BuildSettingsForTranslation(popupSettings, ocrResult.Text);
        var translationResult = ocrResult.Success
            ? await _translationService.TranslateAsync(ocrResult.Text, effectiveSettings, cancellationToken)
            : TranslationResult.FromError("OCR 未成功，已跳过翻译。");

        if (cancellationToken.IsCancellationRequested)
        {
            return "OCR 识别已取消。";
        }

        await _historyStore.AppendAsync(new CaptureTranslationRecord
        {
            WorkflowType = "ocr-translate",
            ImagePath = imagePath,
            SourceText = ocrResult.Text,
            TranslatedText = translationResult.Text,
            OcrError = ocrResult.ErrorMessage,
            OcrDebugInfo = ocrResult.DebugSummary,
            TranslationError = translationResult.ErrorMessage
        });

        var status = BuildTranslateStatus(ocrResult, translationResult);
        if (translationResult.Success)
        {
            popupWindow.UpdateTranslationResult(translationResult.Text, status);
        }
        else
        {
            popupWindow.UpdateFailure(status, translationResult.ErrorMessage);
        }

        popupWindow.ShowAboveSelectionOverlay();
        return status;
    }

    private static void CloseRetainedSelectionChrome(Window? retainedSelectionChromeWindow)
    {
        if (retainedSelectionChromeWindow is null)
        {
            return;
        }

        try
        {
            if (retainedSelectionChromeWindow.IsVisible)
            {
                retainedSelectionChromeWindow.Close();
            }
        }
        catch (InvalidOperationException)
        {
            // The retained action window may already be closing from the user path.
        }
    }

    private TranslationPopupWindow GetOrCreateTranslationPopupWindow(Window? owner)
    {
        if (_translationPopupWindow is not null)
        {
            return _translationPopupWindow;
        }

        var popupWindow = new TranslationPopupWindow(
            "OCR 识别并翻译",
            "正在识别文本...",
            string.Empty,
            string.Empty,
            new AppSettings(),
            null,
            owner,
            null);

        popupWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_translationPopupWindow, popupWindow))
            {
                _translationPopupWindow = null;
            }
        };

        _translationPopupWindow = popupWindow;
        return popupWindow;
    }

    private async Task<string> ExecuteQrCodeAsync(string imagePath, Window? owner, Int32Rect? captureRegion)
    {
        using var timeoutCts = new CancellationTokenSource(QrCodeDecodeTimeout);
        var qrResult = await _qrCodeService.DecodeAsync(imagePath, timeoutCts.Token);
        await _historyStore.AppendAsync(new CaptureTranslationRecord
        {
            WorkflowType = "qr",
            ImagePath = imagePath,
            QrCodeText = qrResult.Text,
            OcrError = qrResult.ErrorMessage
        });

        var status = qrResult.Success ? "二维码识别已完成。" : $"二维码识别失败：{qrResult.ErrorMessage}";
        var popupWindow = new QrCodeResultPopupWindow(
            status,
            qrResult.Success ? qrResult.Text : qrResult.ErrorMessage,
            qrResult.Success,
            captureRegion,
            owner)
        {
            Owner = owner
        };
        popupWindow.Show();

        return status;
    }

    private string ExecuteCopyImage(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            return "复制失败：截图文件不存在。";
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(imagePath);
            bitmap.EndInit();
            bitmap.Freeze();

            Clipboard.SetImage(bitmap);
            return "截图已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            return $"复制失败：{ex.Message}";
        }
    }

    private string ExecuteSave(string imagePath, Window? owner)
    {
        var savedPath = _fileService.SaveToDefaultDirectory(imagePath);
        WpfMessageBox.Show(owner, $"已保存到：\n{savedPath}", "保存成功");
        return $"已保存到默认目录：{savedPath}";
    }

    private string ExecuteSaveAs(string imagePath, Window? owner)
    {
        var savedPath = _fileService.SaveAs(imagePath);
        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return "已取消另存为。";
        }

        WpfMessageBox.Show(owner, $"已保存到：\n{savedPath}", "另存为成功");
        return $"已另存为：{savedPath}";
    }

    private static string BuildTranslateStatus(OcrResult ocrResult, TranslationResult translationResult)
    {
        if (ocrResult.Success && translationResult.Success)
        {
            return "OCR 和翻译已完成。";
        }

        if (!ocrResult.Success)
        {
            return $"OCR 识别失败：{ocrResult.ErrorMessage}";
        }

        return $"翻译失败：{translationResult.ErrorMessage}";
    }
}
