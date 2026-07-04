using System.IO;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SnapCat.App.Services;

public sealed class CapturedImageFileService
{
    public string GetTempDirectoryPath()
    {
        return Path.Combine(Path.GetTempPath(), "SnapCat");
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
        var deletedCount = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
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

        return deletedCount;
    }
}
