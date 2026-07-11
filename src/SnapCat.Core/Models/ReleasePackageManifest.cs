namespace SnapCat.Core.Models;

public sealed class ReleasePackageManifest
{
    public ReleasePackageKind Kind { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
}
