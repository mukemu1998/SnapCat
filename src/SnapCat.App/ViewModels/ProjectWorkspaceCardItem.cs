using System.IO;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed class ProjectWorkspaceCardItem : ObservableObject
{
    private string _editableName;
    private readonly ImageSource? _coverImage;

    public ProjectWorkspaceCardItem(ProjectWorkspaceSummary summary, bool canDelete = true, bool isSelected = false, bool isEditing = false)
    {
        Summary = summary;
        CanDelete = canDelete;
        IsSelected = isSelected;
        IsEditing = isEditing;
        _editableName = summary.Name;
        _coverImage = ImageThumbnailLoader.Load(
            ResolveCoverPath(summary.DirectoryPath, summary.CoverImageRelativePath),
            480);
    }

    public ProjectWorkspaceSummary Summary { get; }

    public bool CanDelete { get; }

    public bool IsSelected { get; }

    public bool IsEditing { get; }

    public ImageSource? CoverImage => _coverImage;

    public string Name => Summary.Name;

    public string EditableName
    {
        get => _editableName;
        set => SetProperty(ref _editableName, value);
    }

    public string UpdatedLabel => $"最近编辑 {Summary.UpdatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    public string AssetSummary => Summary.AssetCount == 0 ? "空项目" : $"{Summary.AssetCount} 个项目素材";

    public bool HasCoverImage => CoverImage is not null;

    private static string? ResolveCoverPath(string directoryPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
            ? candidate
            : null;
    }
}
