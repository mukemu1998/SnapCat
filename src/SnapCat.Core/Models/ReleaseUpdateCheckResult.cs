namespace SnapCat.Core.Models;

public sealed record ReleaseUpdateCheckResult(
    bool IsUpdateAvailable,
    string Message,
    ReleaseUpdateManifest? Manifest = null);
