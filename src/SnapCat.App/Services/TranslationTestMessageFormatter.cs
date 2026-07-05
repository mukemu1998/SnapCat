using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public static class TranslationTestMessageFormatter
{
    public static string BuildTranslationTestResult(string sourceText, TranslationResult result)
    {
        return result.Success
            ? $"翻译测试成功。\n\n原文：\n{sourceText}\n\n译文：\n{result.Text}"
            : $"翻译测试失败。\n\n错误信息：\n{result.ErrorMessage}";
    }

    public static string BuildApiConnectionResult(ApiTranslationProfile? profile, TranslationResult result)
    {
        var common =
            $"配置名称：{SettingsSummaryFormatter.FormatSummaryValue(profile?.Name ?? string.Empty)}\n" +
            $"接口地址：{SettingsSummaryFormatter.FormatSummaryValue(profile?.BaseUrl ?? string.Empty)}\n" +
            $"模型：{SettingsSummaryFormatter.FormatSummaryValue(profile?.Model ?? string.Empty)}";

        return result.Success
            ? $"API 连接测试成功。\n\n{common}\n返回内容：\n{result.Text}"
            : $"API 连接测试失败。\n\n{common}\n错误信息：\n{result.ErrorMessage}";
    }

    public static string BuildApiMissingConfigurationMessage(AppSettings settings)
    {
        return settings.ApiProfiles.Count == 0
            ? "API 连接测试前请先添加一套 API 配置。"
            : "API 连接测试前请先填写完整的 API Key 和模型。";
    }
}
