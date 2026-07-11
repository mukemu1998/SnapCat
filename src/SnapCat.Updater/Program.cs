using System.Diagnostics;

namespace SnapCat.Updater;

internal static class Program
{
    private const int Success = 0;
    private const int InvalidArguments = 2;
    private const int MainProcessTimeout = 3;
    private const int ReplacementFailed = 4;
    private const int RestartFailed = 5;
    private const int ReplacementRetryCount = 40;
    private static readonly TimeSpan ReplacementRetryDelay = TimeSpan.FromMilliseconds(400);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var options = UpdateOptions.Parse(args);
        if (options is null)
        {
            return InvalidArguments;
        }

        var logPath = Path.Combine(Path.GetDirectoryName(options.TargetDirectory)!, "SnapCat-update.log");
        try
        {
            await WaitForMainProcessExitAsync(options.ProcessId, TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            AppendLog(logPath, "等待 SnapCat 退出超时，已取消升级。");
            return MainProcessTimeout;
        }

        var targetDirectory = Path.GetFullPath(options.TargetDirectory);
        var stagedDirectory = Path.GetFullPath(options.StagedDirectory);
        var backupDirectory = targetDirectory + $".backup-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        if (!File.Exists(Path.Combine(stagedDirectory, "SnapCat.exe")))
        {
            AppendLog(logPath, "待升级目录无效：未找到 SnapCat.exe。");
            return InvalidArguments;
        }

        if (!Directory.Exists(targetDirectory) || IsNestedDirectory(stagedDirectory, targetDirectory))
        {
            AppendLog(logPath, "目标目录或待升级目录无效。");
            return InvalidArguments;
        }

        try
        {
            // Explorer, the tray shell, or security software can keep a file handle briefly after
            // the main process exits. Retrying here avoids failing an otherwise valid update.
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            await ReplaceApplicationDirectoryAsync(targetDirectory, stagedDirectory, backupDirectory, logPath);
        }
        catch (Exception exception)
        {
            AppendLog(logPath, $"替换应用文件失败：{exception.Message}");
            return ReplacementFailed;
        }

        try
        {
            var executablePath = Path.Combine(targetDirectory, "SnapCat.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = targetDirectory,
                UseShellExecute = true
            });
            TryDeleteDirectory(backupDirectory);
            AppendLog(logPath, "升级完成，已重新启动 SnapCat。");
            return Success;
        }
        catch (Exception exception)
        {
            try
            {
                TryDeleteDirectory(targetDirectory);
                Directory.Move(backupDirectory, targetDirectory);
            }
            catch (Exception rollbackException)
            {
                AppendLog(logPath, $"重新启动失败且回滚失败：{rollbackException.Message}");
            }

            AppendLog(logPath, $"重新启动失败，已尝试回滚：{exception.Message}");
            return RestartFailed;
        }
    }

    private static async Task WaitForMainProcessExitAsync(int processId, TimeSpan timeout)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (ArgumentException)
        {
            // The process has already exited.
        }
    }

    private static async Task ReplaceApplicationDirectoryAsync(
        string targetDirectory,
        string stagedDirectory,
        string backupDirectory,
        string logPath)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= ReplacementRetryCount; attempt++)
        {
            var targetMoved = false;
            try
            {
                Directory.Move(targetDirectory, backupDirectory);
                targetMoved = true;
                Directory.Move(stagedDirectory, targetDirectory);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastFailure = exception;
                if (targetMoved && !Directory.Exists(targetDirectory) && Directory.Exists(backupDirectory))
                {
                    try
                    {
                        Directory.Move(backupDirectory, targetDirectory);
                    }
                    catch (Exception rollbackException)
                    {
                        throw new IOException("替换失败且无法恢复原程序目录。", rollbackException);
                    }
                }

                if (attempt == ReplacementRetryCount)
                {
                    break;
                }

                AppendLog(logPath, $"应用目录暂时被占用，正在重试（{attempt}/{ReplacementRetryCount}）。");
                await Task.Delay(ReplacementRetryDelay);
            }
        }

        throw new IOException("应用文件在主程序退出后仍被占用，请关闭可能正在访问 SnapCat 目录的程序后重试。", lastFailure);
    }

    private static bool IsNestedDirectory(string childDirectory, string parentDirectory)
    {
        var parent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return childDirectory.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void AppendLog(string logPath, string message)
    {
        try
        {
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // A logging failure must never block the updater.
        }
    }

    private sealed record UpdateOptions(int ProcessId, string StagedDirectory, string TargetDirectory)
    {
        public static UpdateOptions? Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index + 1 < args.Length; index += 2)
            {
                values[args[index]] = args[index + 1];
            }

            return values.TryGetValue("--process-id", out var processText)
                   && int.TryParse(processText, out var processId)
                   && values.TryGetValue("--staged-directory", out var stagedDirectory)
                   && values.TryGetValue("--target-directory", out var targetDirectory)
                   && !string.IsNullOrWhiteSpace(stagedDirectory)
                   && !string.IsNullOrWhiteSpace(targetDirectory)
                ? new UpdateOptions(processId, stagedDirectory, targetDirectory)
                : null;
        }
    }
}
