namespace SnapCat.Core.Models;

/// <summary>
/// Describes how an asset entered a SnapCat project without tying it to a UI flow.
/// </summary>
public enum ProjectAssetKind
{
    Imported,
    Screenshot,
    Generated,
    Reference
}
