namespace SnapCat.Core.Models;

public sealed class SnapCatProject
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "SnapCat 项目";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public List<ProjectAsset> Assets { get; set; } = [];

    public List<ProjectAssetCollection> Collections { get; set; } = [];
}
