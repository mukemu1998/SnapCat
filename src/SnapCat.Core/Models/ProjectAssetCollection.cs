namespace SnapCat.Core.Models;

/// <summary>
/// A lightweight, reusable set of project assets. Collections store IDs only, so they survive project moves.
/// </summary>
public sealed class ProjectAssetCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public List<string> AssetIds { get; set; } = [];
}
