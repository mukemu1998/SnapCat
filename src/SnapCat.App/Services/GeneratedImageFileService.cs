using System.IO;

namespace SnapCat.App.Services;

public sealed class GeneratedImageFileService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
        ".bmp"
    };

    private readonly string _directoryPath;

    public GeneratedImageFileService(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        _directoryPath = Path.Combine(userDataDirectory, "generated");
    }

    public string GetDirectoryPath() => _directoryPath;

    public IReadOnlyList<string> GetImagePaths()
    {
        if (!Directory.Exists(_directoryPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(_directoryPath, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedImage)
            .OrderByDescending(File.GetLastWriteTime)
            .ToList();
    }

    public async Task<string> SaveAsync(
        string? sourceFileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);
        var extension = Path.GetExtension(sourceFileName) ?? string.Empty;
        if (!SupportedExtensions.Contains(extension))
        {
            extension = ".png";
        }

        var path = Path.Combine(_directoryPath, $"SnapCat-{DateTime.Now:yyyyMMdd-HHmmssfff}{extension}");
        if (File.Exists(path))
        {
            path = Path.Combine(_directoryPath, $"SnapCat-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}{extension}");
        }

        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }

    public int DeleteFiles(IEnumerable<string> paths)
    {
        var deletedCount = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!IsManagedImage(path) || !File.Exists(path))
                {
                    continue;
                }

                File.Delete(path);
                deletedCount++;
            }
            catch
            {
                // Files opened by another program are left in place and reported by the caller's count.
            }
        }

        return deletedCount;
    }

    public int DeleteAll() => DeleteFiles(GetImagePaths());

    private bool IsManagedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsSupportedImage(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        return string.Equals(
            directory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(_directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));
}
