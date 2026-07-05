using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public sealed record HotkeyValidationSummary(bool HasIssue, string Text);

public static class HotkeyValidationFormatter
{
    public static HotkeyValidationSummary Build(
        IReadOnlyDictionary<string, string> hotkeys,
        IEnumerable<HotkeyRegistrationResult> registrationResults)
    {
        var duplicates = hotkeys
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToList();

        var registrationFailures = registrationResults
            .Where(static result => !result.IsRegistered)
            .ToList();

        if (duplicates.Count == 0 && registrationFailures.Count == 0)
        {
            var hasAnyHotkey = hotkeys.Any(static pair => !string.IsNullOrWhiteSpace(pair.Value));
            return new HotkeyValidationSummary(
                false,
                hasAnyHotkey ? "当前快捷键没有发现重复冲突。" : "当前没有设置可选快捷键，需要时可在上方录制。");
        }

        var messages = duplicates.Select(group =>
        {
            var labels = string.Join("、", group.Select(static pair => pair.Key));
            return $"快捷键冲突：{labels} 都使用了 {group.Key}";
        });

        messages = messages.Concat(registrationFailures.Select(FormatRegistrationFailure));
        return new HotkeyValidationSummary(true, string.Join(Environment.NewLine, messages));
    }

    private static string FormatRegistrationFailure(HotkeyRegistrationResult result)
    {
        return $"注册失败：{result.Label} ({SettingsSummaryFormatter.FormatSummaryValue(result.HotkeyText)}) - {result.Message}";
    }
}
