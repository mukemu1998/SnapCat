namespace SnapCat.Core.Models;

/// <summary>
/// A resolved asset mention from user-authored prompt text.
/// </summary>
public sealed record ProjectAssetReferenceMatch(string Token, ProjectAsset Asset);
