using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using SnapCat.App.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private const string ProjectHomeUrl = "https://github.com/mukemu1998/SnapCat";
    private const string ProjectIssuesUrl = "https://github.com/mukemu1998/SnapCat/issues";
    private const string ProjectReleasesUrl = "https://github.com/mukemu1998/SnapCat/releases";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/mukemu1998/SnapCat/releases/latest";

    private static readonly HttpClient UpdateCheckHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private void OpenProjectHomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenUrl(ProjectHomeUrl, "GitHub 仓库");
    }

    private void OpenIssuesButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenUrl(ProjectIssuesUrl, "GitHub Issue");
    }

    private void OpenReleasesButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenUrl(ProjectReleasesUrl, "Releases 下载页");
    }

    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        AboutUpdateStatusTextBlock.Text = "正在检查 GitHub Releases...";
        StatusTextBlock.Text = "正在检查更新...";
        AppendOperationLog("开始检查 GitHub Releases 更新。");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.UserAgent.ParseAdd("SnapCat");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await UpdateCheckHttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                AboutUpdateStatusTextBlock.Text = $"检查失败：GitHub 返回 {(int)response.StatusCode}。可以手动打开下载页查看。";
                StatusTextBlock.Text = "检查更新失败。";
                AppendOperationLog($"检查更新失败：GitHub 返回 {(int)response.StatusCode}。");
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var latestTag = document.RootElement.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;
            var latestName = document.RootElement.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            var latestPage = document.RootElement.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? ProjectReleasesUrl
                : ProjectReleasesUrl;

            var currentVersion = GetAppVersion();
            var latestVersionText = string.IsNullOrWhiteSpace(latestTag)
                ? SettingsSummaryFormatter.FormatSummaryValue(latestName)
                : latestTag;

            AboutUpdateStatusTextBlock.Text = IsRemoteVersionNewer(currentVersion, latestVersionText)
                ? $"发现新版本：{latestVersionText}。当前版本：{currentVersion}。可打开下载页手动覆盖升级：{latestPage}"
                : $"当前已是最新或不低于最新发布版。当前版本：{currentVersion}，最新发布：{latestVersionText}。";
            StatusTextBlock.Text = "检查更新完成。";
            AppendOperationLog($"检查更新完成：当前 {currentVersion}，最新 {latestVersionText}。");
        }
        catch (Exception ex)
        {
            AboutUpdateStatusTextBlock.Text = $"检查更新失败：{ex.Message}。可以手动打开下载页查看。";
            StatusTextBlock.Text = "检查更新失败。";
            AppendOperationLog($"检查更新失败：{ex.Message}");
        }
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.3.3-preview";

        return TrimVersionMetadata(version);
    }

    private static string TrimVersionMetadata(string version)
    {
        return version.Split('+', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? version;
    }

    private static bool IsRemoteVersionNewer(string currentVersionText, string remoteVersionText)
    {
        var currentVersion = ParseVersion(currentVersionText);
        var remoteVersion = ParseVersion(remoteVersionText);

        return currentVersion is not null
            && remoteVersion is not null
            && remoteVersion > currentVersion;
    }

    private static Version? ParseVersion(string value)
    {
        var normalized = value.Trim()
            .TrimStart('v', 'V')
            .Split(['+', '-'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return Version.TryParse(normalized, out var version) ? version : null;
    }
}
