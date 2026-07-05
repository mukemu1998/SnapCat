using System.Windows;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;

namespace SnapCat.App;

public partial class MainWindow
{
    private void ConfigureSections()
    {
        _sections[MainSection.OcrSettings] = new NavigationSectionMetadata(
            OcrNavButton,
            OcrSection,
            "OCR 设置",
            "这里集中管理 OCR 引擎、本地路径和识别测试，不再弹出新的设置窗口。");

        _sections[MainSection.TranslationSettings] = new NavigationSectionMetadata(
            TranslationNavButton,
            TranslationSection,
            "翻译设置",
            "这里集中管理接口地址、模型、目标语言和翻译测试，默认翻译与自定义 API 都从这里处理。");

        _sections[MainSection.History] = new NavigationSectionMetadata(
            HistoryNavButton,
            HistorySection,
            "历史记录",
            "这里直接查看截图历史、结果预览和图片位置，双击或点按钮可继续查看完整详情。");

        _sections[MainSection.PinnedImages] = new NavigationSectionMetadata(
            PinnedImagesNavButton,
            PinnedImagesSection,
            "贴图管理",
            "这里管理当前屏幕上的贴图窗口、贴图组和显示状态。");

        _sections[MainSection.ScreenshotManagement] = new NavigationSectionMetadata(
            ScreenshotManagementNavButton,
            ScreenshotManagementSection,
            "截图管理",
            "这里管理默认截图目录、临时文件目录和过期数据清理。");

        _sections[MainSection.ExecuteActions] = new NavigationSectionMetadata(
            ExecuteActionsNavButton,
            ExecuteActionsSection,
            "执行操作与快捷键",
            "这里集中管理可直接执行的自由框选命令和贴图快捷键，每个命令可以独立绑定快捷键。");

        _sections[MainSection.CaptureSettings] = new NavigationSectionMetadata(
            CaptureSettingsNavButton,
            CaptureSettingsSection,
            "系统快捷键",
            "这里整理主窗口、托盘和系统级操作快捷键；截图与贴图命令快捷键在“执行操作与快捷键”目录里设置。");

        _sections[MainSection.AppearanceSettings] = new NavigationSectionMetadata(
            AppearanceSettingsNavButton,
            AppearanceSettingsSection,
            "外观主题",
            "这里集中调整 SnapCat 的界面配色主题，切换后会立即同步到主菜单、托盘菜单和翻译浮窗。");

        _sections[MainSection.TraySettings] = new NavigationSectionMetadata(
            TraySettingsNavButton,
            TraySettingsSection,
            "托盘与启动",
            "这里整理托盘左键动作、开机自启和启动方式，系统行为都在当前窗口查看和修改。");

        _sections[MainSection.UserConfig] = new NavigationSectionMetadata(
            UserConfigNavButton,
            UserConfigSection,
            "用户配置",
            "这里管理 SnapCat 的用户配置保存位置，快捷键、主题、API、历史记录和贴图布局都默认保存在用户目录。");

        _sections[MainSection.Status] = new NavigationSectionMetadata(
            StatusNavButton,
            StatusSection,
            "运行状态",
            "这里查看当前配置摘要、环境检查和当前主窗口的整体工作状态。");

        _sections[MainSection.About] = new NavigationSectionMetadata(
            AboutNavButton,
            AboutSection,
            "关于 SnapCat",
            "这里查看版本、开源信息、反馈入口和更新检查。");
    }

    private void NavigationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string tag)
        {
            return;
        }

        if (Enum.TryParse<MainSection>(tag, out var section))
        {
            SelectSection(section);
        }
    }

    private void SelectSection(MainSection section)
    {
        _currentSection = section;

        foreach (var pair in _sections)
        {
            var isSelected = pair.Key == section;
            pair.Value.Content.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            ApplyNavigationStyle(pair.Value.Button, isSelected);
        }

        var selected = _sections[section];
        SectionTitleTextBlock.Text = selected.Title;
        SectionDescriptionTextBlock.Text = selected.Description;

        if (section == MainSection.PinnedImages)
        {
            RefreshPinnedImagesList();
        }

        if (section == MainSection.ScreenshotManagement)
        {
            RenderScreenshotManagementInfo();
        }

        if (section == MainSection.UserConfig)
        {
            RenderUserConfigLocationInfo();
        }

        if (section == MainSection.About)
        {
            AboutVersionTextBlock.Text = $"版本 {GetAppVersion()}";
        }
    }

    private void ApplyNavigationStyle(WpfButton button, bool isSelected)
    {
        button.Background = isSelected
            ? GetThemeBrush("Theme.Brush.Accent")
            : WpfBrushes.Transparent;
        button.BorderBrush = isSelected
            ? GetThemeBrush("Theme.Brush.AccentBorder")
            : WpfBrushes.Transparent;
        button.Foreground = isSelected
            ? GetThemeBrush("Theme.Brush.TextPrimary")
            : GetThemeBrush("Theme.Brush.TextPrimary");
        button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private WpfBrush GetThemeBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as WpfBrush ?? WpfBrushes.Transparent;
    }

    private enum MainSection
    {
        OcrSettings,
        TranslationSettings,
        History,
        ScreenshotManagement,
        PinnedImages,
        ExecuteActions,
        CaptureSettings,
        AppearanceSettings,
        TraySettings,
        UserConfig,
        Status,
        About
    }
}
