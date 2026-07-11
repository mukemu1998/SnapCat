using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SnapCat.Core.Models;

public sealed class AiProviderProfile : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _protocol = AiProviderProtocol.OpenAiCompatible;
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private bool _isEnabled = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Protocol
    {
        get => _protocol;
        set => SetField(ref _protocol, value);
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set => SetField(ref _baseUrl, value);
    }

    public string ApiKey
    {
        get => _apiKey;
        set => SetField(ref _apiKey, value);
    }

    public string Model
    {
        get => _model;
        set => SetField(ref _model, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetField(ref _isEnabled, value);
    }

    public AiModelCapabilities Capabilities { get; set; } = AiModelCapabilities.None;

    public int MaxReferenceImageCount { get; set; } = 1;

    public int MaxOutputCount { get; set; } = 1;

    public bool SupportsCostEstimate { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
