using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed record ApiProfileEditorDraft(
    string Name,
    string BaseUrl,
    string ApiKey,
    string Model,
    string SystemPrompt,
    bool EnableContext)
{
    public static ApiProfileEditorDraft Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        AppSettings.DefaultSystemPrompt,
        false);
}
