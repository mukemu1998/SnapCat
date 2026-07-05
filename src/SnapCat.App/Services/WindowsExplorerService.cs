using System.Diagnostics;
using System.IO;

namespace SnapCat.App.Services;

public enum ExplorerOpenResult
{
    FileSelected,
    DirectoryOpened,
    Missing
}

public static class WindowsExplorerService
{
    public static ExplorerOpenResult OpenFileOrContainingDirectory(string path)
    {
        if (File.Exists(path))
        {
            StartExplorer($"/select,\"{path}\"");
            return ExplorerOpenResult.FileSelected;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            OpenDirectory(directory);
            return ExplorerOpenResult.DirectoryOpened;
        }

        return ExplorerOpenResult.Missing;
    }

    public static void OpenDirectory(string directory, bool createIfMissing = false)
    {
        if (createIfMissing)
        {
            Directory.CreateDirectory(directory);
        }

        StartExplorer($"\"{directory}\"");
    }

    private static void StartExplorer(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }
}
