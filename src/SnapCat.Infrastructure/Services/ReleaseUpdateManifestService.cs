using System.Text.Json;
using System.Text.Json.Serialization;
using SnapCat.Core.Models;

namespace SnapCat.Infrastructure.Services;

public sealed class ReleaseUpdateManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public ReleaseUpdateManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("更新清单为空。");
        }

        var manifest = JsonSerializer.Deserialize<ReleaseUpdateManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("更新清单格式无效。");
        Validate(manifest);
        return manifest;
    }

    public void Validate(ReleaseUpdateManifest manifest)
    {
        _ = ReleaseVersionForValidation(manifest.Version);
        if (manifest.Packages.Count == 0)
        {
            throw new InvalidDataException("更新清单没有可下载的安装包。");
        }

        foreach (var package in manifest.Packages)
        {
            if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var packageUri)
                || packageUri.Scheme is not ("https" or "http"))
            {
                throw new InvalidDataException("更新包下载地址无效。");
            }

            if (package.Sha256.Length != 64 || package.Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException("更新包 SHA256 校验值无效。");
            }

            if (package.SizeBytes <= 0)
            {
                throw new InvalidDataException("更新包大小无效。");
            }
        }
    }

    private static Version ReleaseVersionForValidation(string version)
    {
        var normalized = version?.Trim().TrimStart('v', 'V') ?? string.Empty;
        var separator = normalized.IndexOf('-');
        var numeric = separator >= 0 ? normalized[..separator] : normalized;
        return Version.TryParse(numeric, out var parsed)
            ? parsed
            : throw new InvalidDataException("更新清单版本号无效。");
    }
}
