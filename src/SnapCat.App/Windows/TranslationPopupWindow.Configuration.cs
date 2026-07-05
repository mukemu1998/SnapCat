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
    private void ConfigureApiProfiles()
    {
        _settings.NormalizeApiProfiles();
        _isApplyingApiProfileSelection = true;
        _viewModel.RefreshApiProfiles();

        if (_settings.ApiProfiles.Count == 0)
        {
            ApiProfileComboBox.Visibility = Visibility.Collapsed;
            _isApplyingApiProfileSelection = false;
            return;
        }

        _viewModel.SelectedApiProfileId = _settings.SelectedApiProfileId;
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
        UpdateViewModelTranslationContext();
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

    private void UpdateViewModelTranslationContext()
    {
        _viewModel.SelectedProviderLabel = GetSelectedProviderLabel();
    }
}
