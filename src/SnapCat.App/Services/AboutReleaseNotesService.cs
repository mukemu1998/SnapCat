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
            "0.3.4-preview" => new AboutReleaseNotes(
                "本版更新：框选标注流程整理",
                "这一版重点修复等待菜单重复标注状态，并补齐框选标注快捷键、确认复制、独立另存和色板交互。",
                [
                    "再次进入标注时会基于当前选框原图重新编辑，不再叠加上一次标注结果。",
                    "标注完成动作拆分为“确定并复制”和“另存为”，完成后返回等待菜单。",
                    "新增“自由框选并标注”执行命令，可独立绑定并持久化全局快捷键。",
                    "色板会贴近工具栏并自动避让屏幕边缘，工具栏和色板空白区域支持拖动。",
                    "调整后的等待选框会延续到后续操作，减少重复调整。"
                ]),
            "0.3.3-preview" => new AboutReleaseNotes(
                "本版更新：画布标注、马赛克和托盘提示增强",
                "这一版重点整理全屏画布与框选标注、真实像素马赛克、托盘悬浮提示和快捷键显示，让常用操作更直观。",
                [
                    "马赛克改为基于截图内容的真实像素化，遮挡效果更接近真正打码。",
                    "托盘悬浮改为原生提示，标题带版本号，并支持自定义显示两条组合指令摘要。",
                    "快捷键显示会把 Oem3 等内部键名转换为真实键位符号，主菜单和托盘提示保持一致。",
                    "托盘相关文案统一为“单击托盘”，并优化托盘摘要设置区域布局。",
                    "继续打磨画布标注、贴图菜单、旋转编辑、弹窗圆角和翻译浮窗语言切换。"
                ]),
            "0.3.2-preview" => new AboutReleaseNotes(
                "本版更新：贴图菜单与截图复制流程整理",
                "这一版重点收束上一轮 OCR、二维码、截图复制和贴图右键菜单体验，让常用操作更短，低频操作更安全。",
                [
                    "新增“自由框选并复制到剪贴板”组合指令，可单独绑定快捷键，框选后直接复制截图。",
                    "二维码识别改为贴近选框的小结果窗，支持一键复制，并修复识别时可能卡住的问题。",
                    "Windows 高质量文本提取在小选区失败时支持手动补框后继续接入翻译浮窗。",
                    "贴图右键菜单精简为常用一级操作，编辑、阵列、分组和更多操作折叠收纳。",
                    "贴图阵列输入框和右键子菜单进一步收窄，降低菜单遮挡。"
                ]),
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
