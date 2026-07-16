using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SnapCat.Core.Models;

/// <summary>
/// Stores a user-owned image generation backend. Credentials are persisted only by the local settings store.
/// </summary>
public sealed class ImageGenerationProfile : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _protocol = ImageGenerationProtocol.ComfyUi;
    private string _baseUrl = "http://127.0.0.1:8188";
    private string _apiKey = string.Empty;
    private string _defaultCheckpoint = string.Empty;
    private bool _isEnabled = true;
    private bool _isDefault;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get => _name; set => SetField(ref _name, value); }

    public string Protocol { get => _protocol; set => SetField(ref _protocol, value); }

    public string BaseUrl { get => _baseUrl; set => SetField(ref _baseUrl, value); }

    public string ApiKey { get => _apiKey; set => SetField(ref _apiKey, value); }

    public string DefaultCheckpoint { get => _defaultCheckpoint; set => SetField(ref _defaultCheckpoint, value); }

    public bool IsEnabled { get => _isEnabled; set => SetField(ref _isEnabled, value); }

    public bool IsDefault { get => _isDefault; set => SetField(ref _isDefault, value); }

    public int DefaultWidth { get; set; } = 1024;

    public int DefaultHeight { get; set; } = 1024;

    public int DefaultSteps { get; set; } = 24;

    public double DefaultCfgScale { get; set; } = 7d;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Normalize(int index)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        Name = string.IsNullOrWhiteSpace(Name) ? $"生图配置 {index + 1}" : Name.Trim();
        Protocol = ImageGenerationProtocol.Normalize(Protocol);
        BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "http://127.0.0.1:8188" : BaseUrl.Trim().TrimEnd('/');
        DefaultCheckpoint = DefaultCheckpoint?.Trim() ?? string.Empty;
        DefaultWidth = NormalizeDimension(DefaultWidth, 1024);
        DefaultHeight = NormalizeDimension(DefaultHeight, 1024);
        DefaultSteps = Math.Clamp(DefaultSteps, 1, 150);
        DefaultCfgScale = Math.Clamp(DefaultCfgScale, 1d, 30d);
    }

    public static List<ImageGenerationProfile> CloneAll(IEnumerable<ImageGenerationProfile>? profiles)
    {
        return profiles?.Select(profile => new ImageGenerationProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Protocol = profile.Protocol,
            BaseUrl = profile.BaseUrl,
            ApiKey = profile.ApiKey,
            DefaultCheckpoint = profile.DefaultCheckpoint,
            IsEnabled = profile.IsEnabled,
            IsDefault = profile.IsDefault,
            DefaultWidth = profile.DefaultWidth,
            DefaultHeight = profile.DefaultHeight,
            DefaultSteps = profile.DefaultSteps,
            DefaultCfgScale = profile.DefaultCfgScale
        }).ToList() ?? [];
    }

    private static int NormalizeDimension(int value, int fallback)
    {
        var normalized = value <= 0 ? fallback : Math.Clamp(value, 256, 4096);
        return normalized - (normalized % 8);
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

