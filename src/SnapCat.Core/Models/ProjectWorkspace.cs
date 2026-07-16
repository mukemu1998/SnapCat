namespace SnapCat.Core.Models;

/// <summary>
/// Keeps project metadata together with its on-disk root. The root never enters project.json.
/// </summary>
public sealed class ProjectWorkspace
{
    public required string DirectoryPath { get; init; }

    public required SnapCatProject Project { get; init; }

    public string ProjectFilePath => Path.Combine(DirectoryPath, "project.json");
}
