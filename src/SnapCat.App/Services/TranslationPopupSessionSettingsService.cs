using SnapCat.Core.Models;

namespace SnapCat.App.Services;

public static class TranslationPopupSessionSettingsService
{
    public static AppSettings CreateInitialSettings(AppSettings defaultSettings)
    {
        var settings = TranslationLanguageHelper.CloneSettings(defaultSettings);
        settings.NormalizeApiProfiles();
        settings.SyncLegacyApiFieldsFromSelectedProfile();
        return settings;
    }

    public static AppSettings CreateReuseSettings(AppSettings defaultSettings, AppSettings currentSessionSettings)
    {
        var settings = CreateInitialSettings(defaultSettings);
        var sessionProvider = currentSessionSettings.TranslationProviderPreference;

        if (string.Equals(sessionProvider, TranslationProviderPreference.Local, StringComparison.OrdinalIgnoreCase))
        {
            settings.TranslationProviderPreference = TranslationProviderPreference.Local;
            return settings;
        }

        if (!string.Equals(sessionProvider, TranslationProviderPreference.Api, StringComparison.OrdinalIgnoreCase))
        {
            return settings;
        }

        settings.TranslationProviderPreference = TranslationProviderPreference.Api;
        var sessionApiProfileId = currentSessionSettings.SelectedApiProfileId;
        if (settings.ApiProfiles.Any(profile => string.Equals(profile.Id, sessionApiProfileId, StringComparison.Ordinal)))
        {
            settings.SelectedApiProfileId = sessionApiProfileId;
        }

        settings.SyncLegacyApiFieldsFromSelectedProfile();
        return settings;
    }

    public static AppSettings CreateExecutionSnapshot(AppSettings sessionSettings)
    {
        var settings = TranslationLanguageHelper.CloneSettings(sessionSettings);
        settings.NormalizeApiProfiles();
        settings.SyncLegacyApiFieldsFromSelectedProfile();
        return settings;
    }
}
