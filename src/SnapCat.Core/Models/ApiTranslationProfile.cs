namespace SnapCat.Core.Models;

public sealed class ApiTranslationProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string SystemPrompt { get; set; } = AppSettings.DefaultSystemPrompt;

    public bool EnableContext { get; set; }
}
