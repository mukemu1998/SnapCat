using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

/// <summary>
/// Owns the portable project folder format. Project files contain no credentials or absolute paths.
/// </summary>
public sealed class ProjectWorkspaceService : IProjectWorkspaceService
{
    private const string ProjectFileName = "project.json";
    private const string StateFileName = "project-workspace-state.json";
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".webp"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _stateFilePath;

    public ProjectWorkspaceService(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        DefaultProjectsDirectory = Path.Combine(userDataDirectory, "projects");
        _stateFilePath = Path.Combine(userDataDirectory, StateFileName);
    }

    public string DefaultProjectsDirectory { get; }

    public async Task<string?> GetLastOpenedProjectDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_stateFilePath);
            var state = await JsonSerializer.DeserializeAsync<ProjectWorkspaceState>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            var directory = state?.LastOpenedProjectDirectory;
            return !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, ProjectFileName))
                ? Path.GetFullPath(directory)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ProjectWorkspace> CreateAsync(
        string parentDirectory,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        var normalizedName = NormalizeProjectName(projectName);
        var root = CreateUniqueProjectDirectory(parentDirectory, normalizedName);
        Directory.CreateDirectory(root);
        EnsureProjectDirectories(root);

        var now = DateTimeOffset.Now;
        var workspace = new ProjectWorkspace
        {
            DirectoryPath = root,
            Project = new SnapCatProject
            {
                Name = normalizedName,
                CreatedAt = now,
                UpdatedAt = now
            }
        };

        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public async Task<ProjectWorkspace> OpenAsync(string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        var root = Path.GetFullPath(projectDirectory);
        var projectFilePath = Path.Combine(root, ProjectFileName);
        if (!File.Exists(projectFilePath))
        {
            throw new FileNotFoundException("未找到 SnapCat 项目文件 project.json。", projectFilePath);
        }

        await using var stream = File.OpenRead(projectFilePath);
        var project = await JsonSerializer.DeserializeAsync<SnapCatProject>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("项目文件为空或格式无效。");

        NormalizeProject(project);
        EnsureProjectDirectories(root);
        var workspace = new ProjectWorkspace { DirectoryPath = root, Project = project };
        await SaveLastOpenedProjectDirectoryAsync(root, cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public async Task SaveAsync(ProjectWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        NormalizeProject(workspace.Project);
        EnsureProjectDirectories(workspace.DirectoryPath);
        workspace.Project.UpdatedAt = DateTimeOffset.Now;
        await WriteJsonAtomicallyAsync(workspace.ProjectFilePath, workspace.Project, cancellationToken).ConfigureAwait(false);
        await SaveLastOpenedProjectDirectoryAsync(workspace.DirectoryPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProjectAsset> ImportImageAsync(
        ProjectWorkspace workspace,
        string sourcePath,
        ProjectAssetKind kind = ProjectAssetKind.Imported,
        ProjectAssetCategory category = ProjectAssetCategory.Unclassified,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到需要导入的图片。", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedImageExtensions.Contains(extension))
        {
            throw new InvalidDataException("仅支持 PNG、JPG、JPEG、BMP 或 WEBP 图片。" );
        }

        EnsureProjectDirectories(workspace.DirectoryPath);
        var assetId = Guid.NewGuid().ToString("N");
        var storageDirectoryName = GetStorageDirectoryName(kind);
        var storedFileName = assetId + extension.ToLowerInvariant();
        var relativePath = Path.Combine(storageDirectoryName, storedFileName);
        var destinationPath = GetSafeProjectPath(workspace.DirectoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await CopyFileAsync(sourcePath, destinationPath, cancellationToken).ConfigureAwait(false);

        var thumbnailRelativePath = Path.Combine("thumbnails", assetId + ".png");
        var thumbnailPath = GetSafeProjectPath(workspace.DirectoryPath, thumbnailRelativePath);
        TryCreateThumbnail(destinationPath, thumbnailPath);

        var asset = new ProjectAsset
        {
            Id = assetId,
            DisplayName = Path.GetFileName(sourcePath),
            Kind = kind,
            Category = category,
            RelativePath = relativePath,
            ThumbnailRelativePath = File.Exists(thumbnailPath) ? thumbnailRelativePath : string.Empty,
            CreatedAt = DateTimeOffset.Now,
            Version = 1
        };

        workspace.Project.Assets.Insert(0, asset);
        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return asset;
    }

    public async Task<ProjectAsset> CreateDerivedAssetAsync(
        ProjectWorkspace workspace,
        string parentAssetId,
        string sourcePath,
        ProjectAssetKind kind = ProjectAssetKind.Generated,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentAssetId);
        var parent = workspace.Project.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Id, parentAssetId, StringComparison.Ordinal));
        if (parent is null)
        {
            throw new KeyNotFoundException("找不到派生资产的原始素材。");
        }

        var asset = await ImportImageAsync(
            workspace,
            sourcePath,
            kind,
            parent.Category,
            cancellationToken).ConfigureAwait(false);
        asset.ParentAssetId = parent.Id;
        asset.Version = parent.Version + 1;
        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return asset;
    }

    public async Task<ProjectAssetCollection> CreateCollectionAsync(
        ProjectWorkspace workspace,
        string name,
        IEnumerable<string>? assetIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var collection = new ProjectAssetCollection
        {
            Name = NormalizeCollectionName(name),
            AssetIds = NormalizeCollectionAssetIds(workspace.Project, assetIds)
        };
        workspace.Project.Collections.Add(collection);
        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return collection;
    }

    public async Task UpdateCollectionAssetsAsync(
        ProjectWorkspace workspace,
        string collectionId,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionId);
        ArgumentNullException.ThrowIfNull(assetIds);
        var collection = workspace.Project.Collections.FirstOrDefault(item =>
            string.Equals(item.Id, collectionId, StringComparison.Ordinal));
        if (collection is null)
        {
            throw new KeyNotFoundException("找不到需要更新的项目素材集合。");
        }

        collection.AssetIds = NormalizeCollectionAssetIds(workspace.Project, assetIds);
        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> MoveToRecycleBinAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetIds);
        var selectedIds = assetIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (selectedIds.Count == 0)
        {
            return 0;
        }

        var recycleDirectory = Path.Combine(workspace.DirectoryPath, "recycle-bin");
        Directory.CreateDirectory(recycleDirectory);
        var removedAssets = workspace.Project.Assets
            .Where(asset => selectedIds.Contains(asset.Id))
            .ToList();

        foreach (var asset in removedAssets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MoveProjectFileToRecycleBin(workspace.DirectoryPath, recycleDirectory, asset.RelativePath, asset.Id);
            MoveProjectFileToRecycleBin(workspace.DirectoryPath, recycleDirectory, asset.ThumbnailRelativePath, asset.Id);
            var metadataPath = Path.Combine(recycleDirectory, $"{asset.Id}.json");
            var recycleEntry = new ProjectRecycleBinEntry
            {
                Asset = asset,
                CollectionIds = workspace.Project.Collections
                    .Where(collection => collection.AssetIds.Contains(asset.Id, StringComparer.Ordinal))
                    .Select(static collection => collection.Id)
                    .ToList()
            };
            await WriteJsonAtomicallyAsync(metadataPath, recycleEntry, cancellationToken).ConfigureAwait(false);
            workspace.Project.Assets.Remove(asset);
        }

        if (removedAssets.Count > 0)
        {
            var removedIds = removedAssets.Select(static asset => asset.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var collection in workspace.Project.Collections)
            {
                collection.AssetIds.RemoveAll(removedIds.Contains);
            }
        }

        if (removedAssets.Count > 0)
        {
            await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        }

        return removedAssets.Count;
    }

    public async Task<int> RestoreFromRecycleBinAsync(
        ProjectWorkspace workspace,
        IEnumerable<string> assetIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetIds);
        var requestedIds = assetIds.Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        if (requestedIds.Count == 0)
        {
            return 0;
        }

        var recycleDirectory = Path.Combine(workspace.DirectoryPath, "recycle-bin");
        if (!Directory.Exists(recycleDirectory))
        {
            return 0;
        }

        var restoredCount = 0;
        foreach (var assetId in requestedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (workspace.Project.Assets.Any(asset => string.Equals(asset.Id, assetId, StringComparison.Ordinal)))
            {
                continue;
            }

            var metadataPath = Path.Combine(recycleDirectory, $"{assetId}.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            var recycleEntry = await ReadRecycleBinEntryAsync(metadataPath, cancellationToken).ConfigureAwait(false);
            var asset = recycleEntry?.Asset;
            if (asset is null || !string.Equals(asset.Id, assetId, StringComparison.Ordinal))
            {
                continue;
            }

            NormalizeAsset(asset);
            if (!RestoreProjectFileFromRecycleBin(workspace.DirectoryPath, recycleDirectory, asset.RelativePath, assetId))
            {
                continue;
            }
            RestoreProjectFileFromRecycleBin(workspace.DirectoryPath, recycleDirectory, asset.ThumbnailRelativePath, assetId);
            workspace.Project.Assets.Insert(0, asset);
            foreach (var collection in workspace.Project.Collections.Where(collection =>
                         recycleEntry!.CollectionIds.Contains(collection.Id, StringComparer.Ordinal)))
            {
                if (!collection.AssetIds.Contains(asset.Id, StringComparer.Ordinal))
                {
                    collection.AssetIds.Add(asset.Id);
                }
            }
            File.Delete(metadataPath);
            restoredCount++;
        }

        if (restoredCount > 0)
        {
            await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        }

        return restoredCount;
    }

    public async Task<IReadOnlyList<ProjectAsset>> GetRecycledAssetsAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var recycleDirectory = Path.Combine(workspace.DirectoryPath, "recycle-bin");
        if (!Directory.Exists(recycleDirectory))
        {
            return [];
        }

        var activeAssetIds = workspace.Project.Assets.Select(static asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var assets = new List<ProjectAsset>();
        foreach (var metadataPath in Directory.EnumerateFiles(recycleDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var recycleEntry = await ReadRecycleBinEntryAsync(metadataPath, cancellationToken).ConfigureAwait(false);
                var asset = recycleEntry?.Asset;
                if (asset is null || activeAssetIds.Contains(asset.Id))
                {
                    continue;
                }

                assets.Add(NormalizeAsset(asset));
            }
            catch
            {
                // Invalid recycle-bin metadata must not block usable project assets.
            }
        }

        return assets
            .GroupBy(static asset => asset.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderByDescending(static asset => asset.CreatedAt)
            .ToList();
    }

    public async Task<string> CreateBackupAsync(
        ProjectWorkspace workspace,
        string destinationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);

        var targetDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(targetDirectory);
        var safeName = NormalizeProjectName(workspace.Project.Name);
        var backupPath = Path.Combine(targetDirectory, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        for (var suffix = 2; File.Exists(backupPath); suffix++)
        {
            backupPath = Path.Combine(targetDirectory, $"{safeName}-{DateTime.Now:yyyyMMdd-HHmmss}-{suffix}.zip");
        }

        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Create);
            foreach (var sourcePath in Directory.EnumerateFiles(workspace.DirectoryPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(workspace.DirectoryPath, sourcePath);
                if (IsCachePath(relativePath))
                {
                    continue;
                }

                archive.CreateEntryFromFile(sourcePath, relativePath, CompressionLevel.Optimal);
            }
        }, cancellationToken).ConfigureAwait(false);

        return backupPath;
    }

    private static void NormalizeProject(SnapCatProject project)
    {
        project.FormatVersion = SnapCatProject.CurrentFormatVersion;
        project.Id = string.IsNullOrWhiteSpace(project.Id) ? Guid.NewGuid().ToString("N") : project.Id.Trim();
        project.Name = NormalizeProjectName(project.Name);
        project.Assets ??= [];
        project.Collections ??= [];
        project.Assets = project.Assets
            .Where(static asset => asset is not null)
            .Select(NormalizeAsset)
            .GroupBy(static asset => asset.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        var validAssetIds = project.Assets.Select(static asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        project.Collections = project.Collections
            .Where(static collection => collection is not null)
            .Select(collection => NormalizeCollection(collection, validAssetIds))
            .GroupBy(static collection => collection.Id, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
    }

    private static ProjectAsset NormalizeAsset(ProjectAsset asset)
    {
        asset.Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id.Trim();
        asset.DisplayName = string.IsNullOrWhiteSpace(asset.DisplayName) ? "未命名素材" : asset.DisplayName.Trim();
        asset.RelativePath = NormalizeRelativePath(asset.RelativePath);
        asset.ThumbnailRelativePath = NormalizeRelativePath(asset.ThumbnailRelativePath);
        asset.Version = Math.Max(1, asset.Version);
        asset.ParentAssetId = string.IsNullOrWhiteSpace(asset.ParentAssetId) ? null : asset.ParentAssetId.Trim();
        return asset;
    }

    private static ProjectAssetCollection NormalizeCollection(ProjectAssetCollection collection, ISet<string> validAssetIds)
    {
        collection.Id = string.IsNullOrWhiteSpace(collection.Id) ? Guid.NewGuid().ToString("N") : collection.Id.Trim();
        collection.Name = NormalizeCollectionName(collection.Name);
        collection.AssetIds = collection.AssetIds?
            .Where(id => !string.IsNullOrWhiteSpace(id) && validAssetIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? [];
        return collection;
    }

    private static List<string> NormalizeCollectionAssetIds(SnapCatProject project, IEnumerable<string>? assetIds) =>
        (assetIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Where(id => project.Assets.Any(asset => string.Equals(asset.Id, id, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string NormalizeCollectionName(string? name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(normalized) ? "未命名素材集合" : normalized[..Math.Min(normalized.Length, 80)];
    }

    private static string NormalizeProjectName(string? name)
    {
        var compact = string.Join(' ', (name ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = "SnapCat 项目";
        }

        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safe = new string(compact.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "SnapCat 项目" : safe[..Math.Min(safe.Length, 80)];
    }

    private static string CreateUniqueProjectDirectory(string parentDirectory, string projectName)
    {
        var parent = Path.GetFullPath(parentDirectory);
        Directory.CreateDirectory(parent);
        var candidate = Path.Combine(parent, projectName);
        for (var suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(parent, $"{projectName} {suffix}");
        }

        return candidate;
    }

    private static void EnsureProjectDirectories(string root)
    {
        Directory.CreateDirectory(root);
        foreach (var directory in new[] { "originals", "generated", "references", "thumbnails", "cache", "recycle-bin" })
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }
    }

    private static string GetStorageDirectoryName(ProjectAssetKind kind) => kind switch
    {
        ProjectAssetKind.Generated => "generated",
        ProjectAssetKind.Reference => "references",
        _ => "originals"
    };

    private static bool IsCachePath(string relativePath) =>
        relativePath.StartsWith($"cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || relativePath.StartsWith($"cache{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelativePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        var normalized = relativePath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Any(static segment => segment == "..")
            ? string.Empty
            : normalized;
    }

    private static string GetSafeProjectPath(string projectRoot, string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            throw new InvalidDataException("项目素材路径无效。");
        }

        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("项目素材路径越出了项目目录。");
        }

        return candidate;
    }

    private static async Task<ProjectRecycleBinEntry?> ReadRecycleBinEntryAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(metadataPath);
        var entry = await JsonSerializer.DeserializeAsync<ProjectRecycleBinEntry>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(entry?.Asset.Id))
        {
            return entry;
        }

        // Older recycle-bin records stored the asset directly.
        stream.Position = 0;
        var asset = await JsonSerializer.DeserializeAsync<ProjectAsset>(stream, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        return asset is null ? null : new ProjectRecycleBinEntry { Asset = asset };
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static void TryCreateThumbnail(string sourcePath, string thumbnailPath)
    {
        try
        {
            using var source = Image.FromFile(sourcePath);
            var scale = Math.Min(256d / source.Width, 256d / source.Height);
            var width = Math.Max(1, (int)Math.Round(source.Width * Math.Min(1d, scale)));
            var height = Math.Max(1, (int)Math.Round(source.Height * Math.Min(1d, scale)));
            using var thumbnail = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(thumbnail);
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.DrawImage(source, 0, 0, width, height);
            thumbnail.Save(thumbnailPath, ImageFormat.Png);
        }
        catch
        {
            // Unsupported files still remain usable as source assets; the UI falls back to the original image.
        }
    }

    private static void MoveProjectFileToRecycleBin(string root, string recycleDirectory, string relativePath, string assetId)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        var sourcePath = GetSafeProjectPath(root, relativePath);
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var targetName = $"{assetId}-{Path.GetFileName(sourcePath)}";
        var targetPath = Path.Combine(recycleDirectory, targetName);
        if (File.Exists(targetPath))
        {
            targetPath = Path.Combine(recycleDirectory, $"{assetId}-{Guid.NewGuid():N}-{Path.GetFileName(sourcePath)}");
        }

        File.Move(sourcePath, targetPath);
    }

    private static bool RestoreProjectFileFromRecycleBin(string root, string recycleDirectory, string relativePath, string assetId)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return true;
        }

        var destinationPath = GetSafeProjectPath(root, relativePath);
        if (File.Exists(destinationPath))
        {
            throw new IOException("无法恢复项目素材：目标位置已存在同名文件。");
        }

        var originalFileName = Path.GetFileName(destinationPath);
        var sourcePath = Directory.EnumerateFiles(recycleDirectory, $"{assetId}-*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => path.EndsWith($"-{originalFileName}", StringComparison.OrdinalIgnoreCase));
        if (sourcePath is null)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Move(sourcePath, destinationPath);
        return true;
    }

    private async Task SaveLastOpenedProjectDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        var state = new ProjectWorkspaceState { LastOpenedProjectDirectory = Path.GetFullPath(directoryPath) };
        await WriteJsonAtomicallyAsync(_stateFilePath, state, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T payload, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    private sealed class ProjectWorkspaceState
    {
        public string LastOpenedProjectDirectory { get; set; } = string.Empty;
    }

    private sealed class ProjectRecycleBinEntry
    {
        public ProjectAsset Asset { get; set; } = new();

        public List<string> CollectionIds { get; set; } = [];
    }
}
