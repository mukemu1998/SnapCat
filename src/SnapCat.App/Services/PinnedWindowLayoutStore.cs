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

    public PinnedWindowLayoutStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _filePath = Path.Combine(appDataDirectory, "pinned-windows.json");
    }

    public List<PinnedWindowSnapshot> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            using var stream = File.OpenRead(_filePath);
            return JsonSerializer.Deserialize<List<PinnedWindowSnapshot>>(stream, SerializerOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void Save(IEnumerable<PinnedWindowSnapshot> snapshots)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(_filePath);
        JsonSerializer.Serialize(stream, snapshots.ToList(), SerializerOptions);
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
