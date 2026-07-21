using System.Text.Json;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

/// <summary>
/// Persists regular AI canvas state beside project.json without machine-specific paths.
/// </summary>
public sealed class ProjectAiCanvasWorkspaceService : IAiCanvasWorkspaceService
{
    private const string CanvasFileName = "canvas.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public async Task<AiCanvasDocument> LoadAsync(ProjectWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var path = GetCanvasFilePath(workspace);
        if (!File.Exists(path))
        {
            return CreateDefaultDocument(workspace);
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<AiCanvasDocument>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            return NormalizeDocument(document, workspace);
        }
        catch (JsonException)
        {
            return CreateDefaultDocument(workspace);
        }
    }

    public async Task SaveAsync(ProjectWorkspace workspace, AiCanvasDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(document);
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalized = NormalizeDocument(document, workspace);
            normalized.UpdatedAt = DateTimeOffset.Now;
            var path = GetCanvasFilePath(workspace);
            var temporaryPath = path + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<AiCanvasDocument> AddAssetNodesAsync(
        ProjectWorkspace workspace,
        AiCanvasDocument document,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assetIds);
        var normalized = NormalizeDocument(document, workspace);
        var projectAssetIds = workspace.Project.Assets.Select(static asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var requestedAssetIds = assetIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Where(projectAssetIds.Contains)
            .ToArray();
        normalized.ReferenceAssetIds = normalized.ReferenceAssetIds
            .Concat(requestedAssetIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var existingAssetIds = normalized.AssetNodes.Select(static node => node.AssetId).ToHashSet(StringComparer.Ordinal);
        foreach (var assetId in requestedAssetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!existingAssetIds.Add(assetId))
            {
                continue;
            }

            var index = normalized.AssetNodes.Count;
            normalized.AssetNodes.Add(new AiCanvasAssetNode
            {
                AssetId = assetId,
                X = 64d + (index % 4) * 272d,
                Y = 64d + (index / 4) * 212d,
                ZIndex = index
            });
        }

        await SaveAsync(workspace, normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    public async Task<AiCanvasDocument> SetReferenceAssetsAsync(
        ProjectWorkspace workspace,
        AiCanvasDocument document,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(assetIds);
        var normalized = NormalizeDocument(document, workspace);
        var projectAssetIds = workspace.Project.Assets.Select(static asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        normalized.ReferenceAssetIds = assetIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Where(projectAssetIds.Contains)
            .ToList();
        await SaveAsync(workspace, normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    private static string GetCanvasFilePath(ProjectWorkspace workspace) => Path.Combine(workspace.DirectoryPath, CanvasFileName);

    private static AiCanvasDocument CreateDefaultDocument(ProjectWorkspace workspace) => new()
    {
        Name = $"{workspace.Project.Name} AI 创作画布"
    };

    private static AiCanvasDocument NormalizeDocument(AiCanvasDocument? document, ProjectWorkspace workspace)
    {
        document ??= CreateDefaultDocument(workspace);
        document.FormatVersion = AiCanvasDocument.CurrentFormatVersion;
        document.Id = string.IsNullOrWhiteSpace(document.Id) ? Guid.NewGuid().ToString("N") : document.Id.Trim();
        document.Name = string.IsNullOrWhiteSpace(document.Name) ? $"{workspace.Project.Name} AI 创作画布" : document.Name.Trim();
        document.ViewportScale = double.IsFinite(document.ViewportScale) ? Math.Clamp(document.ViewportScale, 0.25d, 3d) : 1d;
        document.ViewportOffsetX = double.IsFinite(document.ViewportOffsetX) ? document.ViewportOffsetX : 0d;
        document.ViewportOffsetY = double.IsFinite(document.ViewportOffsetY) ? document.ViewportOffsetY : 0d;
        var projectAssetIds = workspace.Project.Assets.Select(static asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        document.ReferenceAssetIds ??= [];
        document.ReferenceAssetIds = document.ReferenceAssetIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Where(projectAssetIds.Contains)
            .ToList();
        document.GenerationDraft ??= new AiCanvasGenerationDraft();
        document.GenerationDraft.Prompt ??= string.Empty;
        document.GenerationDraft.NegativePrompt ??= string.Empty;
        document.GenerationDraft.AspectRatio = NormalizeAspectRatio(document.GenerationDraft.AspectRatio);
        document.GenerationDraft.ReferenceIntent = string.IsNullOrWhiteSpace(document.GenerationDraft.ReferenceIntent)
            ? "综合参考"
            : document.GenerationDraft.ReferenceIntent.Trim();
        document.GenerationDraft.OutputCount = Math.Clamp(document.GenerationDraft.OutputCount, 1, 8);
        document.AssetNodes ??= [];
        document.AssetNodes = document.AssetNodes
            .Where(static node => !string.IsNullOrWhiteSpace(node.AssetId))
            .GroupBy(static node => node.AssetId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Select((node, index) => NormalizeNode(node, index))
            .ToList();
        return document;
    }

    private static string NormalizeAspectRatio(string? value) => value?.Trim() switch
    {
        "16:9" => "16:9",
        "9:16" => "9:16",
        "4:3" => "4:3",
        "3:4" => "3:4",
        _ => "1:1"
    };

    private static AiCanvasAssetNode NormalizeNode(AiCanvasAssetNode node, int index)
    {
        node.Id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id.Trim();
        node.AssetId = node.AssetId.Trim();
        node.X = double.IsFinite(node.X) ? node.X : 0d;
        node.Y = double.IsFinite(node.Y) ? node.Y : 0d;
        node.Width = double.IsFinite(node.Width) ? Math.Clamp(node.Width, 96d, 4096d) : 240d;
        node.Height = double.IsFinite(node.Height) ? Math.Clamp(node.Height, 72d, 4096d) : 180d;
        node.ZIndex = index;
        return node;
    }
}
