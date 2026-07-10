using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;

namespace SnapCat.App.Services;

internal static class SettingsSummaryFormatter
{
    public static string BuildSettingsSummary(AppSettings settings, string userDataDirectory)
    {
        settings.NormalizeApiProfiles();
        var selectedProfile = settings.GetSelectedApiProfile();
        var maskedKey = string.IsNullOrWhiteSpace(selectedProfile?.ApiKey)
            ? "未填写"
            : $"{selectedProfile.ApiKey[..Math.Min(6, selectedProfile.ApiKey.Length)]}...";

        return
            $"API 配置数：{settings.ApiProfiles.Count}\n" +
            $"当前 API 配置：{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)}\n" +
            $"接口地址：{FormatSummaryValue(selectedProfile?.BaseUrl ?? string.Empty)}\n" +
            $"模型：{FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}\n" +
            $"上下文翻译：{(selectedProfile?.EnableContext == true ? "已开启" : "未开启")}\n" +
            $"翻译来源：{FormatTranslationProvider(settings.TranslationProviderPreference)}\n" +
            $"目标语言：{FormatTargetLanguage(settings.TargetLanguage)}\n" +
            $"API Key：{maskedKey}\n" +
            $"OCR：{FormatOcrSummary(settings)}\n" +
            $"翻译：{FormatTranslationSummary(settings)}\n" +
            $"开机自启：{(settings.LaunchAtStartup ? "已开启" : "未开启")}\n" +
            $"快捷键 1（固定到屏幕）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndPin)}\n" +
            $"快捷键 2（OCR 识别）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndOcr)}\n" +
            $"快捷键 3（自动翻译）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndTranslate)}\n" +
            $"快捷键 4（等待操作）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndWaitForAction)}\n" +
            $"快捷键 5（保存截图）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndSave)}\n" +
            $"快捷键 6（复制截图）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndCopy)}\n" +
            $"快捷键 7（框选标注）：{FormatHotkeySummaryValue(settings.HotkeyCaptureAndAnnotate)}\n" +
            $"快捷键 8（全屏画布）：{FormatHotkeySummaryValue(settings.HotkeyFullScreenCanvasEdit)}\n" +
            $"贴图关闭键：{FormatHotkeySummaryValue(settings.PinnedCloseShortcut)}\n" +
            $"贴图隐藏键：{FormatHotkeySummaryValue(settings.PinnedHideShortcut)}\n" +
            $"打开主菜单：{FormatHotkeySummaryValue(settings.HotkeyShowMainWindow)}\n" +
            $"退出软件：{FormatHotkeySummaryValue(settings.HotkeyExitApplication)}\n" +
            $"临时文件保留：{FormatRetentionDays(settings.TempFileRetentionDays)}\n" +
            $"历史记录保留：{FormatRetentionDays(settings.HistoryRetentionDays)}\n" +
            $"单击托盘：{FormatTrayLeftClickAction(settings.TrayLeftClickAction)}\n" +
            $"托盘悬浮摘要 1：{FormatWorkflow(settings.TrayTooltipWorkflowOne)}\n" +
            $"托盘悬浮摘要 2：{FormatWorkflow(settings.TrayTooltipWorkflowTwo)}\n" +
            $"框选入口：{FormatCaptureStartupMode(settings.CaptureStartupMode)}\n" +
            $"用户配置目录：{userDataDirectory}";
    }

    public static string FormatSummaryValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未填写" : value;
    }

    public static string FormatHotkeySummaryValue(string value)
    {
        return FormatSummaryValue(HotkeyTextFormatter.FormatText(value));
    }

    public static string FormatWorkflow(string value)
    {
        return CaptureWorkflowFormatter.ToDisplayName(value);
    }

    public static string FormatCaptureStartupMode(string value)
    {
        return CaptureStartupMode.Normalize(value) == CaptureStartupMode.Live
            ? "直接进入实时框选"
            : "先截取当前屏幕临时画面";
    }

    public static string FormatTrayLeftClickAction(string value)
    {
        return Enum.TryParse<CaptureWorkflowKind>(value, out var action)
            ? action switch
            {
                CaptureWorkflowKind.CaptureAndPin => "自由框选并固定到屏幕",
                CaptureWorkflowKind.CaptureAndOcr => "自由框选后 OCR 识别",
                CaptureWorkflowKind.CaptureAndTranslate => "自由框选后自动翻译",
                CaptureWorkflowKind.CaptureAndSave => "自由框选并保存到默认位置",
                CaptureWorkflowKind.CaptureAndCopy => "自由框选并复制到剪贴板",
                CaptureWorkflowKind.CaptureAndAnnotate => "自由框选并标注",
                CaptureWorkflowKind.FullScreenCanvasEdit => "全屏画布编辑",
                _ => "自由框选后等待操作选择"
            }
            : "自由框选后等待操作选择";
    }

    private static string FormatOcrEngine(string value)
    {
        return value switch
        {
            "windows-text-extractor" => "Windows 高质量文本提取",
            "windows-snipping-clipboard" => "Windows 高质量文本提取",
            "enhanced-windows-ocr" => "本地轻量增强版",
            "windows-media-ocr" => "本地轻量兼容版",
            "enhanced-tesseract" => "Windows 高质量文本提取",
            "tesseract-cli" => "Windows 高质量文本提取",
            _ => value
        };
    }

    private static string FormatOcrSummary(AppSettings settings)
    {
        return settings.OcrEngine switch
        {
            "windows-text-extractor" => "Windows 高质量文本提取（推荐）",
            "windows-snipping-clipboard" => "Windows 高质量文本提取（推荐）",
            "enhanced-windows-ocr" => $"本地轻量增强版（lang={FormatSummaryValue(settings.TesseractLanguage)})",
            "windows-media-ocr" => $"本地轻量兼容版（免安装，lang={FormatSummaryValue(settings.TesseractLanguage)})",
            "enhanced-tesseract" or "tesseract-cli" => "Windows 高质量文本提取（推荐）",
            _ => $"{FormatOcrEngine(settings.OcrEngine)} ({FormatSummaryValue(settings.TesseractExecutablePath)}, lang={FormatSummaryValue(settings.TesseractLanguage)})"
        };
    }

    private static string FormatTranslationSummary(AppSettings settings)
    {
        settings.NormalizeApiProfiles();
        var selectedProfile = settings.GetSelectedApiProfile();

        return SmartTranslationService.GetEffectiveProvider(settings) switch
        {
            TranslationProviderPreference.Local => "本地轻量翻译",
            TranslationProviderPreference.Api => $"API 翻译（{FormatSummaryValue(selectedProfile?.Name ?? string.Empty)} / {FormatSummaryValue(selectedProfile?.Model ?? string.Empty)}）",
            _ => "本地轻量翻译"
        };
    }

    private static string FormatTranslationProvider(string value)
    {
        return value switch
        {
            TranslationProviderPreference.Local => "本地轻量翻译",
            TranslationProviderPreference.Api => "API 翻译",
            _ => "本地轻量翻译"
        };
    }

    private static string FormatTargetLanguage(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "未填写"
            : TranslationLanguageHelper.GetLanguageLabel(value);
    }

    private static string FormatRetentionDays(int days)
    {
        return days <= 0 ? "不自动清理" : $"{days} 天";
    }
}
