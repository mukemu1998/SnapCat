namespace SnapCat.Core.Models;

public sealed class AiProviderProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Protocol { get; set; } = AiProviderProtocol.OpenAiCompatible;

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public AiModelCapabilities Capabilities { get; set; } = AiModelCapabilities.None;

    public int MaxReferenceImageCount { get; set; } = 1;

    public int MaxOutputCount { get; set; } = 1;

    public bool SupportsCostEstimate { get; set; }

    public bool Supports(AiModelCapabilities requiredCapabilities)
    {
        return (Capabilities & requiredCapabilities) == requiredCapabilities;
    }

    public void Normalize(int index)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? $"AI 配置 {index + 1}" : Name.Trim();
        Protocol = AiProviderProtocol.Normalize(Protocol);
        BaseUrl = BaseUrl?.Trim() ?? string.Empty;
        Model = Model?.Trim() ?? string.Empty;
        MaxReferenceImageCount = Math.Clamp(MaxReferenceImageCount, 1, 16);
        MaxOutputCount = Math.Clamp(MaxOutputCount, 1, 16);
    }

    public static List<AiProviderProfile> CloneAll(IEnumerable<AiProviderProfile>? profiles)
    {
        return profiles?.Select(profile => new AiProviderProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Protocol = profile.Protocol,
            BaseUrl = profile.BaseUrl,
            ApiKey = profile.ApiKey,
            Model = profile.Model,
            IsEnabled = profile.IsEnabled,
            Capabilities = profile.Capabilities,
            MaxReferenceImageCount = profile.MaxReferenceImageCount,
            MaxOutputCount = profile.MaxOutputCount,
            SupportsCostEstimate = profile.SupportsCostEstimate
        }).ToList() ?? [];
    }
}
