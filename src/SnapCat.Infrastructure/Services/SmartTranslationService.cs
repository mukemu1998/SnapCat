using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class SmartTranslationService : ITranslationService
{
    private readonly OpenAiCompatibleTranslationService _openAiCompatibleTranslationService;
    private readonly LightweightWebTranslationService _lightweightWebTranslationService;

    public SmartTranslationService(
        OpenAiCompatibleTranslationService openAiCompatibleTranslationService,
        LightweightWebTranslationService lightweightWebTranslationService)
    {
        _openAiCompatibleTranslationService = openAiCompatibleTranslationService;
        _lightweightWebTranslationService = lightweightWebTranslationService;
    }

    public Task<TranslationResult> TranslateAsync(
        string sourceText,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.NormalizeApiProfiles();

        return GetEffectiveProvider(settings) switch
        {
            TranslationProviderPreference.Local => _lightweightWebTranslationService.TranslateAsync(sourceText, settings, cancellationToken),
            TranslationProviderPreference.Api => TranslateByApiAsync(sourceText, settings, cancellationToken),
            _ => _lightweightWebTranslationService.TranslateAsync(sourceText, settings, cancellationToken)
        };
    }

    public static bool HasCustomApiSettings(AppSettings settings)
    {
        settings.NormalizeApiProfiles();
        var profile = settings.GetSelectedApiProfile();
        return profile is not null
            && !string.IsNullOrWhiteSpace(profile.ApiKey)
            && !string.IsNullOrWhiteSpace(profile.Model);
    }

    public static string GetEffectiveProvider(AppSettings settings)
    {
        var preference = settings.TranslationProviderPreference?.Trim();

        return preference switch
        {
            TranslationProviderPreference.Local => TranslationProviderPreference.Local,
            TranslationProviderPreference.Api => TranslationProviderPreference.Api,
            _ => HasCustomApiSettings(settings)
                ? TranslationProviderPreference.Api
                : TranslationProviderPreference.Local
        };
    }

    private Task<TranslationResult> TranslateByApiAsync(
        string sourceText,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!HasCustomApiSettings(settings))
        {
            return Task.FromResult(TranslationResult.FromError("当前已切换到 API 翻译，请先在设置里补全 API Key 和模型。"));
        }

        return _openAiCompatibleTranslationService.TranslateAsync(sourceText, settings, cancellationToken);
    }
}
