namespace SnapCat.Core.Models;

public sealed class ProjectAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public ProjectAssetKind Kind { get; set; } = ProjectAssetKind.Imported;

    public ProjectAssetCategory Category { get; set; } = ProjectAssetCategory.Unclassified;

    // A user-defined label supplements the built-in asset categories without affecting storage paths.
    public string CustomCategory { get; set; } = string.Empty;

    // Paths are always relative to the project root so a project can be moved safely.
    public string RelativePath { get; set; } = string.Empty;

    public string ThumbnailRelativePath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public int Version { get; set; } = 1;

    // Derived assets point to a stable parent asset instead of duplicating path-based links.
    public string? ParentAssetId { get; set; }
}
