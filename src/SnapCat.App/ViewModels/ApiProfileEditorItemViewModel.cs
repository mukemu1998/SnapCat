using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed class ApiProfileEditorItemViewModel : ObservableObject
{
    private string _name = string.Empty;
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private string _systemPrompt = AppSettings.DefaultSystemPrompt;
    private bool _enableContext;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetProperty(ref _baseUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    public string Model
    {
        get => _model;
        set => SetProperty(ref _model, value);
    }

    public string SystemPrompt
    {
        get => _systemPrompt;
        set => SetProperty(ref _systemPrompt, value);
    }

    public bool EnableContext
    {
        get => _enableContext;
        set => SetProperty(ref _enableContext, value);
    }

    public static ApiProfileEditorItemViewModel FromModel(ApiTranslationProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        BaseUrl = profile.BaseUrl,
        ApiKey = profile.ApiKey,
        Model = profile.Model,
        SystemPrompt = profile.SystemPrompt,
        EnableContext = profile.EnableContext
    };

    public ApiProfileEditorDraft ToDraft() => new(
        Name,
        BaseUrl,
        ApiKey,
        Model,
        SystemPrompt,
        EnableContext);

    public void ApplyDraft(ApiProfileEditorDraft draft)
    {
        Name = string.IsNullOrWhiteSpace(draft.Name)
            ? Name
            : draft.Name.Trim();
        BaseUrl = draft.BaseUrl.Trim();
        ApiKey = draft.ApiKey;
        Model = draft.Model.Trim();
        SystemPrompt = string.IsNullOrWhiteSpace(draft.SystemPrompt)
            ? AppSettings.DefaultSystemPrompt
            : draft.SystemPrompt.Trim();
        EnableContext = draft.EnableContext;
    }

    public ApiTranslationProfile ToModel() => new()
    {
        Id = Id,
        Name = Name,
        BaseUrl = BaseUrl,
        ApiKey = ApiKey,
        Model = Model,
        SystemPrompt = SystemPrompt,
        EnableContext = EnableContext
    };
}
