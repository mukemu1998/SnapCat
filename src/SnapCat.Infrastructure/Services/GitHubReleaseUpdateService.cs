using System.Net.Http.Headers;
using System.Text.Json;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class GitHubReleaseUpdateService
{
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReleaseUpdateCheckResult> CheckAsync(
        Uri releasesApiUri,
        string currentVersion,
        ReleasePackageKind packageKind,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateGitHubRequest(releasesApiUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ReleaseUpdateCheckResult(false, $"GitHub 返回 {(int)response.StatusCode}，暂时无法检查更新。");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new ReleaseUpdateCheckResult(false, "GitHub 发布列表格式无效。");
        }

        ReleaseUpdateManifest? newestManifest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (IsDraft(release) || !TryGetString(release, "tag_name", out var version))
            {
                continue;
            }

            try
            {
                if (!ReleaseVersionComparer.IsNewer(version, currentVersion))
                {
                    continue;
                }
            }
            catch (FormatException)
            {
                continue;
            }

            var manifest = await TryBuildManifestAsync(release, version, packageKind, cancellationToken);
            if (manifest is null)
            {
                continue;
            }

            if (newestManifest is null || ReleaseVersionComparer.IsNewer(manifest.Version, newestManifest.Version))
            {
                newestManifest = manifest;
            }
        }

        return newestManifest is null
            ? new ReleaseUpdateCheckResult(false, $"当前已是最新版本：{currentVersion}。")
            : new ReleaseUpdateCheckResult(true, $"发现新版本：{newestManifest.Version}。", newestManifest);
    }

    private async Task<ReleaseUpdateManifest?> TryBuildManifestAsync(
        JsonElement release,
        string version,
        ReleasePackageKind packageKind,
        CancellationToken cancellationToken)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var normalizedVersion = version.TrimStart('v', 'V');
        var kindText = packageKind == ReleasePackageKind.Portable ? "portable" : "runtime-dependent";
        var zipName = $"SnapCat-v{normalizedVersion}-win-x64-{kindText}.zip";
        JsonElement? zipAsset = null;
        JsonElement? shaAsset = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!TryGetString(asset, "name", out var assetName))
            {
                continue;
            }

            if (string.Equals(assetName, zipName, StringComparison.OrdinalIgnoreCase))
            {
                zipAsset = asset;
            }
            else if (string.Equals(assetName, zipName + ".sha256", StringComparison.OrdinalIgnoreCase))
            {
                shaAsset = asset;
            }
        }

        if (zipAsset is null || shaAsset is null
            || !TryGetString(zipAsset.Value, "browser_download_url", out var zipUrl)
            || !TryGetString(shaAsset.Value, "browser_download_url", out var shaUrl))
        {
            return null;
        }

        var sha256 = await DownloadSha256Async(new Uri(shaUrl), cancellationToken);
        if (sha256 is null)
        {
            return null;
        }

        var publishedAt = TryGetString(release, "published_at", out var publishedText)
            && DateTimeOffset.TryParse(publishedText, out var parsedPublishedAt)
            ? parsedPublishedAt
            : DateTimeOffset.MinValue;
        return new ReleaseUpdateManifest
        {
            Version = normalizedVersion,
            Channel = release.TryGetProperty("prerelease", out var preRelease) && preRelease.ValueKind == JsonValueKind.True
                ? "preview"
                : "stable",
            PublishedAt = publishedAt,
            ReleaseNotesUrl = TryGetString(release, "html_url", out var releaseUrl) ? releaseUrl : string.Empty,
            Packages =
            [
                new ReleasePackageManifest
                {
                    Kind = packageKind,
                    DownloadUrl = zipUrl,
                    Sha256 = sha256,
                    SizeBytes = zipAsset.Value.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0
                }
            ]
        };
    }

    private async Task<string?> DownloadSha256Async(Uri shaUri, CancellationToken cancellationToken)
    {
        using var request = CreateGitHubRequest(shaUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var token = (await response.Content.ReadAsStringAsync(cancellationToken))
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token : null;
    }

    private static HttpRequestMessage CreateGitHubRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SnapCat", "1.0"));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        return request;
    }

    private static bool IsDraft(JsonElement release)
    {
        return release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}
