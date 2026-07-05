using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace SnapCat.App.Windows;

public partial class TranslationPopupWindow
{
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

    private sealed record LanguageOption(string Code, string Label);
}
