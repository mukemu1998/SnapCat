using System.IO.Compression;
using System.Security.Cryptography;
using SnapCat.Core.Models;

namespace SnapCat.Infrastructure.Services;

public sealed record UpdateDownloadProgress(long ReceivedBytes, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Clamp(ReceivedBytes * 100d / TotalBytes.Value, 0d, 100d)
        : null;
}

public sealed record ReleasePackageStagingResult(
    string ArchivePath,
    string StagingDirectory,
    long ArchiveSizeBytes,
    string Sha256);

public sealed class ReleaseUpdatePackageService
{
    private readonly HttpClient _httpClient;

    public ReleaseUpdatePackageService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ReleasePackageStagingResult> DownloadAndStageAsync(
        ReleasePackageManifest package,
        string workingDirectory,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        if (!Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var downloadUri))
        {
            throw new InvalidDataException("更新包下载地址无效。");
        }

        Directory.CreateDirectory(workingDirectory);
        var archivePath = Path.Combine(workingDirectory, "package.zip");
        var temporaryArchivePath = archivePath + ".download";
        TryDeleteFile(temporaryArchivePath);

        try
        {
            using var response = await _httpClient.GetAsync(
                downloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var expectedLength = package.SizeBytes > 0 ? package.SizeBytes : response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var receivedBytes = 0L;
            await using (var destination = File.Create(temporaryArchivePath))
            {
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    receivedBytes += read;
                    progress?.Report(new UpdateDownloadProgress(receivedBytes, expectedLength));
                }
            }

            if (package.SizeBytes > 0 && receivedBytes != package.SizeBytes)
            {
                throw new InvalidDataException("更新包下载不完整，文件大小与发布清单不一致。");
            }

            TryDeleteFile(archivePath);
            File.Move(temporaryArchivePath, archivePath);
            return await StageArchiveAsync(archivePath, package, workingDirectory, cancellationToken);
        }
        catch
        {
            TryDeleteFile(temporaryArchivePath);
            throw;
        }
    }

    public async Task<ReleasePackageStagingResult> StageArchiveAsync(
        string archivePath,
        ReleasePackageManifest package,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("找不到已下载的更新包。", archivePath);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        Directory.CreateDirectory(workingDirectory);

        var archiveSize = new FileInfo(archivePath).Length;
        if (package.SizeBytes > 0 && archiveSize != package.SizeBytes)
        {
            throw new InvalidDataException("更新包大小与发布清单不一致。");
        }

        var actualHash = await ComputeSha256Async(archivePath, cancellationToken);
        if (!string.Equals(actualHash, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包校验失败，SHA256 与发布清单不一致。");
        }

        var stagingDirectory = Path.Combine(workingDirectory, $"staged-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destinationPath = GetSafeDestinationPath(stagingDirectory, entry.FullName);
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using var entryStream = entry.Open();
                await using var destinationStream = File.Create(destinationPath);
                await entryStream.CopyToAsync(destinationStream, cancellationToken);
            }

            if (!File.Exists(Path.Combine(stagingDirectory, "SnapCat.exe")))
            {
                throw new InvalidDataException("更新包内容无效：未找到 SnapCat.exe。");
            }

            return new ReleasePackageStagingResult(archivePath, stagingDirectory, archiveSize, actualHash);
        }
        catch
        {
            TryDeleteDirectory(stagingDirectory);
            throw;
        }
    }

    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GetSafeDestinationPath(string rootDirectory, string entryName)
    {
        var normalizedEntryName = entryName.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedEntryName)
            || normalizedEntryName.Split(Path.DirectorySeparatorChar).Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("更新包包含不安全的文件路径。");
        }

        var rootPath = Path.GetFullPath(rootDirectory);
        var destinationPath = Path.GetFullPath(Path.Combine(rootPath, normalizedEntryName));
        var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包包含越界文件路径。");
        }

        return destinationPath;
    }

    private static void TryDeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static void TryDeleteDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
