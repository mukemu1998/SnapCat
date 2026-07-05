using System.IO;
using System.Text.Json;

namespace SnapCat.App.Services;

public sealed class PinnedWindowLayoutStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly string _backupPath;

    public PinnedWindowLayoutStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _filePath = Path.Combine(appDataDirectory, "pinned-windows.json");
        _backupPath = Path.Combine(appDataDirectory, "pinned-windows.backup.json");
    }

    public List<PinnedWindowSnapshot> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            return LoadFromPath(_filePath);
        }
        catch when (File.Exists(_backupPath))
        {
            return LoadFromPath(_backupPath);
        }
    }

    public void Save(IEnumerable<PinnedWindowSnapshot> snapshots)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory!, $"pinned-windows.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, snapshots.ToList(), SerializerOptions);
            }

            _ = LoadFromPath(tempPath);

            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, _backupPath, overwrite: true);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static List<PinnedWindowSnapshot> LoadFromPath(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<PinnedWindowSnapshot>>(stream, SerializerOptions) ?? [];
    }
}

public sealed class PinnedWindowSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string ImagePath { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;

    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}
