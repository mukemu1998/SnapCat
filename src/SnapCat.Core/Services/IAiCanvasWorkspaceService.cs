using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IAiCanvasWorkspaceService
{
    Task<AiCanvasDocument> LoadAsync(ProjectWorkspace workspace, CancellationToken cancellationToken = default);

    Task SaveAsync(
        ProjectWorkspace workspace,
        AiCanvasDocument document,
        CancellationToken cancellationToken = default);

    Task<AiCanvasDocument> SetReferenceAssetsAsync(
        ProjectWorkspace workspace,
        AiCanvasDocument document,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);

    Task<AiCanvasDocument> AddAssetNodesAsync(
        ProjectWorkspace workspace,
        AiCanvasDocument document,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default);
}
