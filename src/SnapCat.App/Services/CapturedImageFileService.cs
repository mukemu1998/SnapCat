using System.IO;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace SnapCat.App.Services;

public sealed class CapturedImageFileService
{
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
}
