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
            $"目标语言：{FormatSummaryValue(settings.TargetLanguage)}\n" +
            $"API Key：{maskedKey}\n" +
            $"OCR：{FormatOcrSummary(settings)}\n" +
            $"翻译：{FormatTranslationSummary(settings)}\n" +
            $"开机自启：{(settings.LaunchAtStartup ? "已开启" : "未开启")}\n" +
            $"快捷键 1（固定到屏幕）：{FormatSummaryValue(settings.HotkeyCaptureAndPin)}\n" +
            $"快捷键 2（自动翻译）：{FormatSummaryValue(settings.HotkeyCaptureAndTranslate)}\n" +
            $"快捷键 3（等待操作）：{FormatSummaryValue(settings.HotkeyCaptureAndWaitForAction)}\n" +
            $"快捷键 4（保存截图）：{FormatSummaryValue(settings.HotkeyCaptureAndSave)}\n" +
            $"贴图关闭键：{FormatSummaryValue(settings.PinnedCloseShortcut)}\n" +
            $"贴图隐藏键：{FormatSummaryValue(settings.PinnedHideShortcut)}\n" +
            $"打开主菜单：{FormatSummaryValue(settings.HotkeyShowMainWindow)}\n" +
            $"退出软件：{FormatSummaryValue(settings.HotkeyExitApplication)}\n" +
            $"临时文件保留：{FormatRetentionDays(settings.TempFileRetentionDays)}\n" +
            $"历史记录保留：{FormatRetentionDays(settings.HistoryRetentionDays)}\n" +
            $"托盘左键：{FormatTrayLeftClickAction(settings.TrayLeftClickAction)}\n" +
            $"用户配置目录：{userDataDirectory}";
    }

    public static string FormatSummaryValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "未填写" : value;
    }

    public static string FormatWorkflow(string value)
    {
        return CaptureWorkflowFormatter.ToDisplayName(value);
    }

    public static string FormatTrayLeftClickAction(string value)
    {
        return Enum.TryParse<CaptureWorkflowKind>(value, out var action)
            ? action switch
            {
                CaptureWorkflowKind.CaptureAndPin => "自由框选并固定到屏幕",
                CaptureWorkflowKind.CaptureAndTranslate => "自由框选后自动翻译",
                CaptureWorkflowKind.CaptureAndSave => "自由框选并保存到默认位置",
                _ => "自由框选后等待操作选择"
            }
            : "自由框选后等待操作选择";
    }

    private static string FormatOcrEngine(string value)
    {
        return value switch
        {
            "windows-media-ocr" => "系统内置 OCR",
            "enhanced-tesseract" => "增强本地 OCR",
            "tesseract-cli" => "兼容模式 OCR",
            _ => value
        };
    }

    private static string FormatOcrSummary(AppSettings settings)
    {
        return settings.OcrEngine switch
        {
            "windows-media-ocr" => $"系统内置 OCR（免安装，lang={FormatSummaryValue(settings.TesseractLanguage)})",
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

    private static string FormatRetentionDays(int days)
    {
        return days <= 0 ? "不自动清理" : $"{days} 天";
    }
}
