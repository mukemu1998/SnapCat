using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using Clipboard = System.Windows.Clipboard;
using FormsScreen = System.Windows.Forms.Screen;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnapCat.App.Windows;

public partial class TranslationPopupWindow : Window
{
    private const double PopupGap = 8;
    private const double PopupVerticalMargin = 24;
    private const double MinimumTranslatedTextBoxHeight = 100;
    private const double TranslatedTextBoxPaddingAllowance = 28;
    private readonly App _app;
    private Int32Rect? _captureRegion;
    private readonly Window? _ownerWindow;
    private AppSettings _settings;
    private Func<Task>? _repeatCaptureAction;
    private bool _hasAnchoredPosition;
    private bool _isApplyingApiProfileSelection;
    public TranslationPopupWindow(
        string title,
        string status,
        string sourceText,
        string translatedText,
        AppSettings settings,
        Int32Rect? captureRegion = null,
        Window? ownerWindow = null,
        Func<Task>? repeatCaptureAction = null)
    {
        InitializeComponent();
        _app = (App)WpfApplication.Current;
        _settings = TranslationLanguageHelper.CloneSettings(settings);
        _settings.NormalizeApiProfiles();
        _captureRegion = captureRegion;
        _ownerWindow = ownerWindow;
        _repeatCaptureAction = repeatCaptureAction;

        Title = title;
        TitleTextBlock.Text = title;
        StatusTextBlock.Text = status;
        SourceTextBox.Text = sourceText;
        TranslatedTextBox.Text = translatedText;

        ConfigureLanguageChoices(sourceText);
        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);
        Loaded += TranslationPopupWindow_OnLoaded;
    }

    public void PrepareForReuse(
        string title,
        AppSettings settings,
        Int32Rect? captureRegion,
        Func<Task>? repeatCaptureAction,
        bool preserveCurrentPosition)
    {
        _settings = TranslationLanguageHelper.CloneSettings(settings);
        _settings.NormalizeApiProfiles();
        _captureRegion = captureRegion;
        _repeatCaptureAction = repeatCaptureAction;
        _hasAnchoredPosition = preserveCurrentPosition;

        Title = title;
        TitleTextBlock.Text = title;
        StatusTextBlock.Text = "正在识别文本...";
        SourceTextBox.Text = string.Empty;
        TranslatedTextBox.Text = string.Empty;
        TranslatedTextBox.Height = double.NaN;

        ConfigureLanguageChoices(string.Empty);
        ConfigureApiProfiles();
        SetTranslationProvider(_settings.TranslationProviderPreference);
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;

        if (IsLoaded)
        {
            ApplyWindowHeightConstraints();
            AdjustTranslatedTextBoxHeight();
            if (!preserveCurrentPosition)
            {
                PositionNearAnchor();
            }
        }
    }

    public void SetBusyState(string status)
    {
        StatusTextBlock.Text = status;
        TranslateButton.IsEnabled = false;
        RecaptureButton.IsEnabled = false;
    }

    public void UpdateRecognizedSource(string sourceText, string status)
    {
        SourceTextBox.Text = sourceText;
        StatusTextBlock.Text = status;
        UpdateDirectionHint();
    }

    public void UpdateTranslationResult(string translatedText, string status)
    {
        TranslatedTextBox.Text = translatedText;
        StatusTextBlock.Text = status;
        TranslateButton.IsEnabled = true;
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        AdjustTranslatedTextBoxHeight();
    }

    public void UpdateFailure(string status, string? translatedText = null)
    {
        if (!string.IsNullOrWhiteSpace(translatedText))
        {
            TranslatedTextBox.Text = translatedText;
        }

        StatusTextBlock.Text = status;
        TranslateButton.IsEnabled = true;
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        AdjustTranslatedTextBoxHeight();
    }

    private void TranslationPopupWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowHeightConstraints();
        AdjustTranslatedTextBoxHeight();
        PositionWindowIfNeeded();
        RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        Activate();
    }

    private void ConfigureLanguageChoices(string sourceText)
    {
        var languages = new[]
        {
            new LanguageOption(TranslationLanguageHelper.AutoLanguage, "自动"),
            new LanguageOption(TranslationLanguageHelper.ChineseSimplified, "简体中文"),
            new LanguageOption(TranslationLanguageHelper.English, "英语"),
            new LanguageOption(TranslationLanguageHelper.Japanese, "日语"),
            new LanguageOption(TranslationLanguageHelper.Korean, "韩语")
        };

        SourceLanguageComboBox.ItemsSource = languages;
        TargetLanguageComboBox.ItemsSource = languages;

        SourceLanguageComboBox.ItemTemplate = (DataTemplate)FindResource("LanguageOptionTemplate");
        TargetLanguageComboBox.ItemTemplate = (DataTemplate)FindResource("LanguageOptionTemplate");
        SourceLanguageComboBox.SelectedValuePath = nameof(LanguageOption.Code);
        TargetLanguageComboBox.SelectedValuePath = nameof(LanguageOption.Code);

        SourceLanguageComboBox.SelectedValue = TranslationLanguageHelper.AutoLanguage;
        TargetLanguageComboBox.SelectedValue = TranslationLanguageHelper.AutoLanguage;
        UpdateDirectionHint();
    }

    private void ConfigureApiProfiles()
    {
        _settings.NormalizeApiProfiles();
        _isApplyingApiProfileSelection = true;
        ApiProfileComboBox.ItemsSource = null;

        if (_settings.ApiProfiles.Count == 0)
        {
            ApiProfileComboBox.Visibility = Visibility.Collapsed;
            _isApplyingApiProfileSelection = false;
            return;
        }

        ApiProfileComboBox.ItemsSource = _settings.ApiProfiles;
        ApiProfileComboBox.SelectedValue = _settings.SelectedApiProfileId;
        _isApplyingApiProfileSelection = false;
        UpdateApiProfileVisibility();
    }

    private void SetTranslationProvider(string? value)
    {
        _settings.NormalizeApiProfiles();
        _settings.TranslationProviderPreference = value switch
        {
            TranslationProviderPreference.Local => TranslationProviderPreference.Local,
            TranslationProviderPreference.Api => TranslationProviderPreference.Api,
            _ => HasCustomApiSettings(_settings)
                ? TranslationProviderPreference.Api
                : TranslationProviderPreference.Local
        };

        _settings.SyncLegacyApiFieldsFromSelectedProfile();
        UpdateProviderButtons();
    }

    private void UpdateProviderButtons()
    {
        ApplyProviderButtonState(LocalProviderButton, TranslationProviderPreference.Local);
        ApplyProviderButtonState(ApiProviderButton, TranslationProviderPreference.Api);
        UpdateApiProfileVisibility();
    }

    private void UpdateApiProfileVisibility()
    {
        ApiProfileComboBox.Visibility =
            string.Equals(_settings.TranslationProviderPreference, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase)
            && _settings.ApiProfiles.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ApplyProviderButtonState(WpfButton button, string provider)
    {
        var isSelected = string.Equals(_settings.TranslationProviderPreference, provider, StringComparison.OrdinalIgnoreCase);
        button.Background = GetThemeBrush(isSelected ? "Theme.Brush.Accent" : "Theme.Brush.ButtonBackground");
        button.BorderBrush = GetThemeBrush(isSelected ? "Theme.Brush.AccentBorder" : "Theme.Brush.ButtonBorder");
    }

    private System.Windows.Media.Brush GetThemeBrush(string resourceKey)
    {
        return TryFindResource(resourceKey) as System.Windows.Media.Brush
            ?? new SolidColorBrush(WpfColor.FromRgb(55, 65, 81));
    }

    private void PositionNearAnchor()
    {
        UpdateLayout();

        var popupWidth = ActualWidth;
        var popupHeight = ActualHeight;
        if (popupWidth <= 0 || popupHeight <= 0)
        {
            popupWidth = Width;
            popupHeight = Math.Max(MinHeight, Height);
        }

        var anchorRectDip = TryGetAnchorRectDip();
        var workAreaDip = GetWorkAreaDip(anchorRectDip);

        var maxX = Math.Max(workAreaDip.Left, workAreaDip.Right - popupWidth);
        var maxY = Math.Max(workAreaDip.Top, workAreaDip.Bottom - popupHeight);

        var centeredX = Clamp(anchorRectDip.Left + (anchorRectDip.Width - popupWidth) / 2, workAreaDip.Left, maxX);
        var centeredY = Clamp(anchorRectDip.Top + (anchorRectDip.Height - popupHeight) / 2, workAreaDip.Top, maxY);

        var candidates = new[]
        {
            new WpfPoint(centeredX, anchorRectDip.Bottom + PopupGap),
            new WpfPoint(centeredX, anchorRectDip.Top - popupHeight - PopupGap),
            new WpfPoint(anchorRectDip.Right + PopupGap, centeredY),
            new WpfPoint(anchorRectDip.Left - popupWidth - PopupGap, centeredY)
        };

        foreach (var candidate in candidates)
        {
            if (candidate.X >= workAreaDip.Left
                && candidate.Y >= workAreaDip.Top
                && candidate.X + popupWidth <= workAreaDip.Right
                && candidate.Y + popupHeight <= workAreaDip.Bottom)
            {
                Left = candidate.X;
                Top = candidate.Y;
                return;
            }
        }

        Left = Clamp(centeredX, workAreaDip.Left, maxX);
        Top = Clamp(anchorRectDip.Bottom + PopupGap, workAreaDip.Top, maxY);
    }

    private WpfRect TryGetAnchorRectDip()
    {
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        if (_captureRegion is not null)
        {
            var topLeft = fromDevice.Transform(new WpfPoint(_captureRegion.Value.X, _captureRegion.Value.Y));
            var bottomRight = fromDevice.Transform(new WpfPoint(
                _captureRegion.Value.X + _captureRegion.Value.Width,
                _captureRegion.Value.Y + _captureRegion.Value.Height));

            return new WpfRect(topLeft, bottomRight);
        }

        if (_ownerWindow is not null)
        {
            return new WpfRect(_ownerWindow.Left, _ownerWindow.Top, _ownerWindow.ActualWidth, _ownerWindow.ActualHeight);
        }

        return new WpfRect(Left, Top, Width, Height);
    }

    private WpfRect GetWorkAreaDip(WpfRect anchorRectDip)
    {
        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var fromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var topLeftPx = toDevice.Transform(new WpfPoint(anchorRectDip.Left, anchorRectDip.Top));
        var bottomRightPx = toDevice.Transform(new WpfPoint(anchorRectDip.Right, anchorRectDip.Bottom));

        var selectionBounds = new Rectangle(
            (int)Math.Round(topLeftPx.X),
            (int)Math.Round(topLeftPx.Y),
            Math.Max(1, (int)Math.Round(bottomRightPx.X - topLeftPx.X)),
            Math.Max(1, (int)Math.Round(bottomRightPx.Y - topLeftPx.Y)));

        var workArea = FormsScreen.FromRectangle(selectionBounds).WorkingArea;
        var workAreaTopLeft = fromDevice.Transform(new WpfPoint(workArea.Left, workArea.Top));
        var workAreaBottomRight = fromDevice.Transform(new WpfPoint(workArea.Right, workArea.Bottom));

        return new WpfRect(workAreaTopLeft, workAreaBottomRight);
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PopupBorder_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInteractiveElement(source))
        {
            return;
        }

        DragMove();
    }

    private async void TranslateButton_OnClick(object sender, RoutedEventArgs e)
    {
        var sourceText = SourceTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            StatusTextBlock.Text = "原文为空，无法执行翻译。";
            return;
        }

        SetBusyState("正在翻译...");

        try
        {
            var effectiveSettings = TranslationLanguageHelper.BuildSettingsForTranslation(
                _settings,
                sourceText,
                TargetLanguageComboBox.SelectedValue as string);

            var result = await _app.TranslationService.TranslateAsync(sourceText, effectiveSettings);
            if (result.Success)
            {
                UpdateTranslationResult(
                    result.Text,
                    $"翻译完成，目标语言：{GetSelectedTargetLanguageLabel()}，来源：{GetSelectedProviderLabel()}");
            }
            else
            {
                UpdateFailure($"翻译失败：{result.ErrorMessage}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            UpdateFailure($"翻译执行失败：{ex.Message}");
        }
    }

    private void LanguageComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateDirectionHint();
    }

    private void SourceTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateDirectionHint();
        }
    }

    private void ProviderButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && button.Tag is string provider)
        {
            SetTranslationProvider(provider);
            StatusTextBlock.Text = $"已切换为{GetSelectedProviderLabel()}。";
        }
    }

    private void ApiProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingApiProfileSelection)
        {
            return;
        }

        _settings.SelectedApiProfileId = ApiProfileComboBox.SelectedValue?.ToString() ?? string.Empty;
        _settings.NormalizeApiProfiles();
        _settings.SyncLegacyApiFieldsFromSelectedProfile();

        if (string.Equals(_settings.TranslationProviderPreference, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase))
        {
            StatusTextBlock.Text = $"已切换 API 配置：{_settings.GetSelectedApiProfile()?.Name ?? "未命名配置"}";
        }
    }

    private void TranslatedTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            AdjustTranslatedTextBoxHeight();
        }
    }

    private string GetAutoTargetLanguage(string sourceText)
    {
        var selectedSourceLanguage = SourceLanguageComboBox.SelectedValue as string;
        if (!string.IsNullOrWhiteSpace(selectedSourceLanguage)
            && !string.Equals(selectedSourceLanguage, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(selectedSourceLanguage, TranslationLanguageHelper.ChineseSimplified, StringComparison.OrdinalIgnoreCase)
                ? TranslationLanguageHelper.English
                : TranslationLanguageHelper.ChineseSimplified;
        }

        return TranslationLanguageHelper.ResolveTargetLanguage(_settings, sourceText);
    }

    private void UpdateDirectionHint()
    {
        var sourceLabel = GetComboLabel(SourceLanguageComboBox);
        var targetLabel = string.Equals(TargetLanguageComboBox.SelectedValue as string, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? GetLanguageLabel(GetAutoTargetLanguage(SourceTextBox.Text?.Trim() ?? string.Empty))
            : GetComboLabel(TargetLanguageComboBox);
        DirectionTextBlock.Text = $"{sourceLabel} -> {targetLabel}";
    }

    private static string GetLanguageLabel(string languageCode)
    {
        return languageCode switch
        {
            TranslationLanguageHelper.ChineseSimplified => "简体中文",
            TranslationLanguageHelper.English => "英语",
            TranslationLanguageHelper.Japanese => "日语",
            TranslationLanguageHelper.Korean => "韩语",
            _ => "自动"
        };
    }

    private static string GetComboLabel(WpfComboBox comboBox)
    {
        return comboBox.SelectedItem is LanguageOption option ? option.Label : "自动";
    }

    private string GetSelectedTargetLanguageLabel()
    {
        return string.Equals(TargetLanguageComboBox.SelectedValue as string, TranslationLanguageHelper.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? GetLanguageLabel(GetAutoTargetLanguage(SourceTextBox.Text?.Trim() ?? string.Empty))
            : GetComboLabel(TargetLanguageComboBox);
    }

    private string GetSelectedProviderLabel()
    {
        return _settings.TranslationProviderPreference switch
        {
            TranslationProviderPreference.Local => "本地翻译",
            TranslationProviderPreference.Api => _settings.GetSelectedApiProfile() is { } profile
                ? $"API 翻译（{profile.Name}）"
                : "API 翻译",
            _ => "本地翻译"
        };
    }

    private static bool HasCustomApiSettings(AppSettings settings)
    {
        settings.NormalizeApiProfiles();
        var profile = settings.GetSelectedApiProfile();
        return profile is not null
            && !string.IsNullOrWhiteSpace(profile.ApiKey)
            && !string.IsNullOrWhiteSpace(profile.Model);
    }

    private void AdjustTranslatedTextBoxHeight()
    {
        Dispatcher.BeginInvoke(() =>
        {
            ApplyWindowHeightConstraints();
            UpdateLayout();

            var desiredHeight = Math.Max(MinimumTranslatedTextBoxHeight, TranslatedTextBox.ExtentHeight + TranslatedTextBoxPaddingAllowance);
            var currentPopupHeight = PopupBorder.ActualHeight > 0 ? PopupBorder.ActualHeight : ActualHeight;
            var currentTranslatedHeight = TranslatedTextBox.ActualHeight > 0 ? TranslatedTextBox.ActualHeight : MinimumTranslatedTextBoxHeight;
            var popupChromeHeight = Math.Max(0d, currentPopupHeight - currentTranslatedHeight);
            var maxTranslatedHeight = Math.Max(MinimumTranslatedTextBoxHeight, MaxHeight - popupChromeHeight - PopupGap);
            var finalHeight = Math.Min(desiredHeight, maxTranslatedHeight);

            TranslatedTextBox.Height = finalHeight;
            var needsOverflowScroll = desiredHeight > finalHeight + 1;
            TranslatedTextBox.VerticalScrollBarVisibility = needsOverflowScroll
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled;

            InvalidateMeasure();
            UpdateLayout();

            if (_hasAnchoredPosition)
            {
                ConstrainWindowToVisibleBounds();
            }
            else
            {
                PositionWindowIfNeeded();
            }
        }, DispatcherPriority.Background);
    }

    private void ApplyWindowHeightConstraints()
    {
        var anchorRectDip = TryGetAnchorRectDip();
        var workAreaDip = GetWorkAreaDip(anchorRectDip);
        MaxHeight = Math.Max(MinHeight, workAreaDip.Height - PopupVerticalMargin);
    }

    private void PositionWindowIfNeeded()
    {
        if (_hasAnchoredPosition)
        {
            return;
        }

        PositionNearAnchor();
        _hasAnchoredPosition = true;
    }

    private void ConstrainWindowToVisibleBounds()
    {
        UpdateLayout();

        var popupWidth = ActualWidth;
        var popupHeight = ActualHeight;
        if (popupWidth <= 0 || popupHeight <= 0)
        {
            return;
        }

        var workAreaDip = GetWorkAreaDip(TryGetAnchorRectDip());
        var maxX = Math.Max(workAreaDip.Left, workAreaDip.Right - popupWidth);
        var maxY = Math.Max(workAreaDip.Top, workAreaDip.Bottom - popupHeight);

        Left = Clamp(Left, workAreaDip.Left, maxX);
        Top = Clamp(Top, workAreaDip.Top, maxY);
    }

    private void CopySourceButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(SourceTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "原文已复制。";
    }

    private async void RecaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_repeatCaptureAction is null)
        {
            return;
        }

        RecaptureButton.IsEnabled = false;

        try
        {
            StatusTextBlock.Text = "请在屏幕上重新框选识别区域。";
            await _repeatCaptureAction();
        }
        finally
        {
            RecaptureButton.IsEnabled = _repeatCaptureAction is not null;
        }
    }

    private void CopyTranslatedButton_OnClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(TranslatedTextBox.Text ?? string.Empty);
        StatusTextBlock.Text = "译文已复制。";
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (max < min)
        {
            return min;
        }

        return Math.Max(min, Math.Min(max, value));
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        DependencyObject? current = source;

        while (current is not null)
        {
            if (current is System.Windows.Controls.Button
                or System.Windows.Controls.TextBox
                or System.Windows.Controls.ComboBox
                or System.Windows.Controls.Primitives.ScrollBar
                or System.Windows.Controls.Primitives.Thumb)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private sealed record LanguageOption(string Code, string Label);
}
