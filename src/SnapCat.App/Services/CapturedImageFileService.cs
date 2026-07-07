using System.IO;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SnapCat.App.Services;

public sealed class CapturedImageFileService
{
    public string GetTempDirectoryPath()
    {
        return Path.Combine(Path.GetTempPath(), "SnapCat");
    }

    public string GetPinnedCacheDirectoryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapCat",
            "pinned-cache");
    }

    public string GetDefaultDirectoryPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "SnapCat");
    }

    public string SaveToDefaultDirectory(string sourceImagePath)
    {
        var directory = GetDefaultDirectoryPath();
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, $"SnapCat-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        File.Copy(sourceImagePath, targetPath, overwrite: true);
        return targetPath;
    }

    public string SaveToPinnedCacheDirectory(string sourceImagePath)
    {
        var directory = GetPinnedCacheDirectoryPath();
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, $"pinned-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");
        File.Copy(sourceImagePath, targetPath, overwrite: true);
        return targetPath;
    }

    public string? SaveAs(string sourceImagePath)
    {
        var dialog = new WpfSaveFileDialog
        {
            Title = "另存为",
            Filter = "PNG 图片|*.png",
            FileName = $"SnapCat-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            AddExtension = true,
            DefaultExt = ".png",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        File.Copy(sourceImagePath, dialog.FileName, overwrite: true);
        return dialog.FileName;
    }

    public int CleanupTempFilesOlderThan(int retentionDays)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var directory = GetTempDirectoryPath();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        return CleanupTempFiles(file => File.GetLastWriteTime(file) < cutoff);
    }

    public int CleanupAllTempFiles()
    {
        return CleanupTempFiles(static _ => true);
    }

    private int CleanupTempFiles(Func<string, bool> shouldDelete)
    {
        var directory = GetTempDirectoryPath();
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var deletedCount = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (shouldDelete(file))
                {
                    File.Delete(file);
                    deletedCount++;
                }
            }
            catch
            {
                // 临时文件可能正被 OCR 或贴图占用，跳过即可。
            }
        }

        DeleteEmptyTempDirectories(directory);
        return deletedCount;
    }

    private static void DeleteEmptyTempDirectories(string directory)
    {
        foreach (var childDirectory in Directory
            .EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
            .OrderByDescending(static path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(childDirectory).Any())
                {
                    Directory.Delete(childDirectory);
                }
            }
            catch
            {
                // 临时目录可能正在被其它流程使用，跳过即可。
            }
        }
    }
}
