namespace SnapCat.Core.Models;

public sealed class ReleaseUpdateManifest
{
    public string Version { get; set; } = string.Empty;

    public string Channel { get; set; } = "preview";

    public DateTimeOffset PublishedAt { get; set; }

    public string ReleaseNotesUrl { get; set; } = string.Empty;

    public List<ReleasePackageManifest> Packages { get; set; } = [];

    public ReleasePackageManifest? GetPackage(ReleasePackageKind kind)
    {
        return Packages.FirstOrDefault(package => package.Kind == kind);
    }
}
