namespace SnapCat.Core.Models;

/// <summary>
/// Read-only project library metadata used to render local project cards without opening a project.
/// </summary>
public sealed class ProjectWorkspaceSummary
{
    public required string DirectoryPath { get; init; }

    public required string ProjectId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public int AssetCount { get; init; }

    // This path is always relative to DirectoryPath and is never persisted outside project.json.
    public string CoverImageRelativePath { get; init; } = string.Empty;
}
