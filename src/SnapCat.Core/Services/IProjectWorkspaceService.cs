using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IProjectWorkspaceService
{
    string DefaultProjectsDirectory { get; }

    string BuiltInDefaultProjectsDirectory { get; }

    Task SetDefaultProjectsDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    Task ResetDefaultProjectsDirectoryAsync(CancellationToken cancellationToken = default);

    Task<string?> GetLastOpenedProjectDirectoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectWorkspaceSummary>> ListLocalProjectsAsync(CancellationToken cancellationToken = default);

    Task DeleteLocalProjectAsync(string projectDirectory, CancellationToken cancellationToken = default);

    Task<ProjectWorkspace> CreateAsync(
        string parentDirectory,
        string projectName,
        CancellationToken cancellationToken = default);

    Task<ProjectWorkspace> OpenAsync(string projectDirectory, CancellationToken cancellationToken = default);

    Task<ProjectWorkspace> RenameAsync(
        ProjectWorkspace workspace,
        string projectName,
        CancellationToken cancellationToken = default);

    Task SaveAsync(ProjectWorkspace workspace, CancellationToken cancellationToken = default);

    Task<ProjectAsset> ImportImageAsync(
        ProjectWorkspace workspace,
        string sourcePath,
        ProjectAssetKind kind = ProjectAssetKind.Imported,
        ProjectAssetCategory category = ProjectAssetCategory.Unclassified,
        CancellationToken cancellationToken = default,
        string? customCategory = null);

    Task<ProjectAsset> CreateDerivedAssetAsync(
        ProjectWorkspace workspace,
        string parentAssetId,
        string sourcePath,
        ProjectAssetKind kind = ProjectAssetKind.Generated,
        CancellationToken cancellationToken = default);

    Task<int> UpdateAssetCategoriesAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        ProjectAssetCategory category,
        CancellationToken cancellationToken = default,
        string? customCategory = null);

    Task<ProjectAssetCollection> CreateCollectionAsync(
        ProjectWorkspace workspace,
        string name,
        IEnumerable<string>? assetIds = null,
        CancellationToken cancellationToken = default);

    Task UpdateCollectionAssetsAsync(
        ProjectWorkspace workspace,
        string collectionId,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);

    Task<int> MoveToRecycleBinAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAssetsPermanentlyAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);

    Task<int> RestoreFromRecycleBinAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);

    Task<int> DeleteFromRecycleBinAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectAsset>> GetRecycledAssetsAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken = default);

    Task<string> CreateBackupAsync(
        ProjectWorkspace workspace,
        string destinationDirectory,
        CancellationToken cancellationToken = default);
}
