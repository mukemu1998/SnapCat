using System.IO;
using System.Text.Json;

namespace SnapCat.App.Services;

public sealed class UserDataLocationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _defaultDirectory;
    private readonly string _locationFilePath;

    public UserDataLocationService()
    {
        _defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapCat");
        Directory.CreateDirectory(_defaultDirectory);
        _locationFilePath = Path.Combine(_defaultDirectory, "config-location.json");
    }

    public string DefaultDirectory => _defaultDirectory;

    public string LocationFilePath => _locationFilePath;

    public bool IsUsingDefaultDirectory(string currentDirectory)
    {
        return string.Equals(
            NormalizePath(currentDirectory),
            NormalizePath(_defaultDirectory),
            StringComparison.OrdinalIgnoreCase);
    }

    public string ResolveUserDataDirectory()
    {
        try
        {
            if (!File.Exists(_locationFilePath))
            {
                return _defaultDirectory;
            }

            var location = JsonSerializer.Deserialize<UserDataLocation>(
                File.ReadAllText(_locationFilePath),
                SerializerOptions);
            var customDirectory = location?.DirectoryPath?.Trim();
            if (string.IsNullOrWhiteSpace(customDirectory))
            {
                return _defaultDirectory;
            }

            Directory.CreateDirectory(customDirectory);
            return customDirectory;
        }
        catch
        {
            return _defaultDirectory;
        }
    }

    public void SaveCustomDirectory(string directoryPath)
    {
        var normalized = NormalizePath(directoryPath);
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, NormalizePath(_defaultDirectory), StringComparison.OrdinalIgnoreCase))
        {
            ResetToDefaultDirectory();
            return;
        }

        Directory.CreateDirectory(normalized);
        var payload = new UserDataLocation
        {
            DirectoryPath = normalized,
            UpdatedAt = DateTimeOffset.Now
        };
        File.WriteAllText(_locationFilePath, JsonSerializer.Serialize(payload, SerializerOptions));
    }

    public void ResetToDefaultDirectory()
    {
        if (File.Exists(_locationFilePath))
        {
            File.Delete(_locationFilePath);
        }
    }

    public void CopyUserData(string sourceDirectory, string targetDirectory)
    {
        var source = NormalizePath(sourceDirectory);
        var target = NormalizePath(targetDirectory);
        if (string.IsNullOrWhiteSpace(source)
            || string.IsNullOrWhiteSpace(target)
            || string.Equals(source, target, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(target);
        CopyKnownFile(source, target, "settings.json");
        CopyKnownFile(source, target, "settings.backup.json");
        CopyKnownFile(source, target, "history.jsonl");
        CopyKnownFile(source, target, "pinned-windows.json");
        CopyKnownFile(source, target, "pinned-windows.backup.json");
        CopyDirectoryIfExists(Path.Combine(source, "logs"), Path.Combine(target, "logs"));
    }

    private static void CopyKnownFile(string sourceDirectory, string targetDirectory, string fileName)
    {
        var sourceFile = Path.Combine(sourceDirectory, fileName);
        if (File.Exists(sourceFile))
        {
            File.Copy(sourceFile, Path.Combine(targetDirectory, fileName), overwrite: true);
        }
    }

    private static void CopyDirectoryIfExists(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(targetDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
            var targetFile = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    private sealed class UserDataLocation
    {
        public string DirectoryPath { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    }
}
