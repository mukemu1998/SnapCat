namespace SnapCat.App.Services;

internal sealed record AboutReleaseNotes(
    string Title,
    string Summary,
    IReadOnlyList<string> Highlights);

internal static class AboutReleaseNotesService
{
    public static AboutReleaseNotes GetForVersion(string version)
    {
        var normalized = NormalizeVersion(version);
        return normalized switch
        {
            "0.3.1-preview" => new AboutReleaseNotes(
                "本版更新：Windows 文本提取链路优化",
                "这一版重点打磨 Windows 高质量文本提取、临时定屏框选和翻译浮窗联动，让 OCR 翻译链路更接近系统级体验。",
                [
                    "Windows 高质量文本提取成为推荐 OCR 链路，组合指令可先定住当前画面再框选识别。",
                    "框选翻译会自动复制识别内容并送入翻译浮窗，再次框选时浮窗保持当前位置。",
                    "等待操作、直接 OCR 和翻译流程统一使用当前框选区域，减少线框切换和跳转感。",
                    "清理未开放的 Tesseract OCR 设置入口，旧配置会自动回落到推荐识别模式。"
                ]),
            "0.3.0-preview" => new AboutReleaseNotes(
                "本版更新：框选与翻译体验增强",
                "这一版主要让框选预识别、放大镜跟随、翻译浮窗和等待菜单更顺手。",
                [
                    "框选时更容易预选窗口、文本、卡片和控件区域。",
                    "颜色放大镜跟随更稳定，右键可直接退出框选模式。",
                    "翻译浮窗支持更多语言朗读，并会记住本次浮窗里选择的翻译来源。",
                    "等待操作菜单重新显示时更清晰，降低拖动后的文字虚焦感。"
                ]),
            _ => new AboutReleaseNotes(
                "本版更新",
                "这里展示当前版本最值得普通用户注意的变化。",
                ["本版包含若干体验优化、稳定性修复和界面细节调整。"])
        };
    }

    private static string NormalizeVersion(string version)
    {
        return version.Trim()
            .TrimStart('v', 'V')
            .Split('+', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?? version.Trim();
    }
}
