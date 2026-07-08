using System.IO;
using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public sealed class StartupDiagnosticsService
{
    public IReadOnlyList<string> BuildWarnings(AppSettings settings)
    {
        var warnings = new List<string>();

        ValidateTesseract(settings, warnings);
        ValidateTranslationSettings(settings, warnings);
        ValidateHotkeys(settings, warnings);

        return warnings;
    }

    private static void ValidateTesseract(AppSettings settings, ICollection<string> warnings)
    {
        if (string.Equals(settings.OcrEngine, "enhanced-windows-ocr", StringComparison.Ordinal)
            || string.Equals(settings.OcrEngine, "windows-text-extractor", StringComparison.Ordinal)
            || string.Equals(settings.OcrEngine, "windows-snipping-clipboard", StringComparison.Ordinal)
            || string.Equals(settings.OcrEngine, "windows-media-ocr", StringComparison.Ordinal))
        {
            if (!string.Equals(settings.OcrEngine, "windows-text-extractor", StringComparison.Ordinal)
                && !string.Equals(settings.OcrEngine, "windows-snipping-clipboard", StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(settings.TesseractLanguage))
            {
                warnings.Add("未填写 OCR 语言，建议至少保留 chi_sim+eng 以便本地轻量 OCR 更好地判断语言。");
            }

            return;
        }

        var executablePath = string.IsNullOrWhiteSpace(settings.TesseractExecutablePath)
            ? "tesseract.exe"
            : settings.TesseractExecutablePath.Trim();

        if (!CanResolveExecutable(executablePath))
        {
            warnings.Add("未找到 Tesseract，可继续使用 Windows 高质量文本提取或本地轻量增强版；如需外部本地识别，再补充 Tesseract 路径。");
        }

        if (string.IsNullOrWhiteSpace(settings.TesseractLanguage))
        {
            warnings.Add("未填写 OCR 语言，建议至少设置为 chi_sim+eng。");
        }
    }

    private static void ValidateTranslationSettings(AppSettings settings, ICollection<string> warnings)
    {
        settings.NormalizeApiProfiles();

        var provider = settings.TranslationProviderPreference?.Trim();
        var hasAnyCustomApiSetting = settings.ApiProfiles.Count > 0;

        if (string.Equals(provider, TranslationProviderPreference.Local, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!hasAnyCustomApiSetting
            && !string.Equals(provider, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var selectedProfile = settings.GetSelectedApiProfile();
        if (selectedProfile is null)
        {
            warnings.Add(string.Equals(provider, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase)
                ? "当前已锁定为 API 翻译，但还没有添加任何 API 配置。"
                : "还没有添加任何 API 配置。当前会改用内置轻量在线翻译。");
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedProfile.ApiKey))
        {
            warnings.Add(string.Equals(provider, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase)
                ? "当前已锁定为 API 翻译，但缺少 API Key。"
                : "翻译接口配置不完整：缺少 API Key。当前会改用内置轻量在线翻译。");
        }

        if (string.IsNullOrWhiteSpace(selectedProfile.Model))
        {
            warnings.Add(string.Equals(provider, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase)
                ? "当前已锁定为 API 翻译，但缺少模型。"
                : "翻译接口配置不完整：缺少模型。当前会改用内置轻量在线翻译。");
        }

        if (string.IsNullOrWhiteSpace(selectedProfile.BaseUrl))
        {
            warnings.Add("未填写接口地址时将使用默认接口地址；如需自定义厂商，请补全完整地址。");
        }
    }

    private static void ValidateHotkeys(AppSettings settings, ICollection<string> warnings)
    {
        var hotkeys = new Dictionary<string, string>
        {
            ["固定到屏幕"] = settings.HotkeyCaptureAndPin,
            ["OCR 识别"] = settings.HotkeyCaptureAndOcr,
            ["自动翻译"] = settings.HotkeyCaptureAndTranslate,
            ["等待操作"] = settings.HotkeyCaptureAndWaitForAction,
            ["保存截图"] = settings.HotkeyCaptureAndSave,
            ["复制截图"] = settings.HotkeyCaptureAndCopy
        };

        var duplicates = hotkeys
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToList();

        foreach (var duplicate in duplicates)
        {
            warnings.Add($"快捷键重复：{duplicate.Key} 被多个动作共用。");
        }

        foreach (var pair in hotkeys)
        {
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (!IsHotkeyFormatLikelyValid(pair.Value))
            {
                warnings.Add($"快捷键格式可能无效：{pair.Key} = {pair.Value}。");
            }
        }
    }

    private static bool CanResolveExecutable(string executablePath)
    {
        if (Path.IsPathRooted(executablePath))
        {
            return File.Exists(executablePath);
        }

        if (File.Exists(executablePath))
        {
            return true;
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, executablePath);
                if (File.Exists(candidate))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return false;
    }

    private static bool IsHotkeyFormatLikelyValid(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (parts.Any(static part => string.IsNullOrWhiteSpace(part)))
        {
            return false;
        }

        var last = parts[^1];
        return last.Length == 1 || Enum.TryParse(last, ignoreCase: true, out System.Windows.Input.Key _);
    }
}
