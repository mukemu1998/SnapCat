using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using SnapCat.App.Services;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private const string ProjectHomeUrl = "https://github.com/mukemu1998/SnapCat";
    private const string ProjectIssuesUrl = "https://github.com/mukemu1998/SnapCat/issues";
    private const string ProjectReleasesUrl = "https://github.com/mukemu1998/SnapCat/releases";
    private const string ReleasesApiUrl = "https://api.github.com/repos/mukemu1998/SnapCat/releases?per_page=20";

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
        await CheckForUpdatesAsync(automatic: false);
    }

    private async Task CheckForUpdatesAsync(bool automatic)
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        _isCheckingUpdates = true;
        if (!automatic)
        {
            AboutUpdateStatusTextBlock.Text = "正在检查 GitHub Releases...";
            StatusTextBlock.Text = "正在检查更新...";
            AppendOperationLog("开始检查 GitHub Releases 更新。");
        }

        try
        {
            var currentVersion = GetAppVersion();
            var result = await _app.GitHubReleaseUpdateService.CheckAsync(
                new Uri(ReleasesApiUrl),
                currentVersion,
                GetCurrentPackageKind());
            if (!automatic || result.IsUpdateAvailable)
            {
                AboutUpdateStatusTextBlock.Text = result.Message;
                StatusTextBlock.Text = result.IsUpdateAvailable ? "发现可用更新。" : "检查更新完成。";
            }

            AppendOperationLog($"检查更新完成：{result.Message}");

            if (!result.IsUpdateAvailable || result.Manifest is null)
            {
                return;
            }

            if (automatic && !IsVisible)
            {
                ShowMainWindow();
            }

            var confirmed = ConfirmDialogWindow.Confirm(
                this,
                "发现新版本",
                $"发现 SnapCat {result.Manifest.Version}。\n\n下载完成后会校验文件、退出当前程序并自动覆盖升级。用户配置、主题、快捷键和 API 信息不会被覆盖。\n\n是否立即下载并升级？",
                "下载并升级",
                "暂不更新");
            if (confirmed)
            {
                await DownloadAndLaunchUpdaterAsync(result.Manifest);
            }
        }
        catch (Exception ex)
        {
            if (!automatic)
            {
                AboutUpdateStatusTextBlock.Text = $"检查更新失败：{ex.Message}。可以手动打开下载页查看。";
                StatusTextBlock.Text = "检查更新失败。";
            }

            AppendOperationLog($"检查更新失败：{ex.Message}");
        }
        finally
        {
            _isCheckingUpdates = false;
        }
    }

    private async Task DownloadAndLaunchUpdaterAsync(ReleaseUpdateManifest manifest)
    {
        var package = manifest.GetPackage(GetCurrentPackageKind());
        if (package is null)
        {
            AboutUpdateStatusTextBlock.Text = "此版本没有适用于当前安装包类型的更新文件。";
            return;
        }

        var applicationDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var parentDirectory = Directory.GetParent(applicationDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            AboutUpdateStatusTextBlock.Text = "无法确定当前程序目录，无法自动升级。";
            return;
        }

        var workingDirectory = Path.Combine(parentDirectory, $".SnapCat-update-{Guid.NewGuid():N}");
        try
        {
            AboutUpdateStatusTextBlock.Text = $"正在下载 SnapCat {manifest.Version}...";
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                AboutUpdateStatusTextBlock.Text = value.Percent is { } percent
                    ? $"正在下载 SnapCat {manifest.Version}：{percent:F0}%"
                    : $"正在下载 SnapCat {manifest.Version}...";
            });
            var staged = await _app.ReleaseUpdatePackageService.DownloadAndStageAsync(
                package,
                workingDirectory,
                progress);

            // Run the updater from outside the staged payload. Otherwise its own EXE would lock
            // the staged directory while it tries to move that directory into the app location.
            var stagedUpdaterSourceDirectory = Path.Combine(staged.StagingDirectory, "Updater");
            var stagedUpdaterSourceExecutable = Path.Combine(stagedUpdaterSourceDirectory, "SnapCat.Updater.exe");
            if (!File.Exists(stagedUpdaterSourceExecutable))
            {
                throw new InvalidDataException("更新包缺少 Updater 更新助手，已取消自动升级。");
            }

            var updaterDirectory = Path.Combine(workingDirectory, "updater-runner");
            CopyDirectory(stagedUpdaterSourceDirectory, updaterDirectory);
            var updaterExecutable = Path.Combine(updaterDirectory, "SnapCat.Updater.exe");
            var processInfo = new ProcessStartInfo
            {
                FileName = updaterExecutable,
                WorkingDirectory = updaterDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processInfo.ArgumentList.Add("--process-id");
            processInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            processInfo.ArgumentList.Add("--staged-directory");
            processInfo.ArgumentList.Add(staged.StagingDirectory);
            processInfo.ArgumentList.Add("--target-directory");
            processInfo.ArgumentList.Add(applicationDirectory);
            Process.Start(processInfo);

            AboutUpdateStatusTextBlock.Text = "更新已校验完成，正在退出 SnapCat 并自动替换文件...";
            StatusTextBlock.Text = "正在启动自动升级。";
            AppendOperationLog($"已准备升级到 {manifest.Version}。\n");
            ExitApplication();
        }
        catch (Exception exception)
        {
            AboutUpdateStatusTextBlock.Text = $"自动升级准备失败：{exception.Message}";
            StatusTextBlock.Text = "自动升级失败。";
            AppendOperationLog($"自动升级准备失败：{exception.Message}");
        }
    }

    private static ReleasePackageKind GetCurrentPackageKind()
    {
        var versionFilePath = Path.Combine(AppContext.BaseDirectory, "VERSION.txt");
        if (File.Exists(versionFilePath))
        {
            var content = File.ReadAllText(versionFilePath);
            if (content.Contains("package_type=runtime-dependent", StringComparison.OrdinalIgnoreCase))
            {
                return ReleasePackageKind.RuntimeDependent;
            }
        }

        return ReleasePackageKind.Portable;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.4.5-preview";

        return TrimVersionMetadata(version);
    }

    private static string TrimVersionMetadata(string version)
    {
        return version.Split('+', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? version;
    }

}
