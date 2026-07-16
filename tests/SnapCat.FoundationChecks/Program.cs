using System.IO.Compression;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using SnapCat.Infrastructure.Services;

var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    "SnapCat.FoundationChecks",
    Guid.NewGuid().ToString("N"));

try
{
    await VerifyAiProfilePersistenceAsync(temporaryDirectory);
    await VerifyImageGenerationProfilePersistenceAsync(temporaryDirectory);
    await VerifySettingsBackupRecoveryAsync(temporaryDirectory);
    await VerifyProjectWorkspaceAsync(temporaryDirectory);
    VerifySettingsSnapshotAndComparison();
    VerifyReleaseUpdateManifest();
    await VerifyDownloadedUpdatePackageStagingAsync(temporaryDirectory);
    await VerifyUpdatePackageStagingAsync(temporaryDirectory);
    VerifyTaskStateMachine();
    await VerifyComfyUiGenerationAsync();
    Console.WriteLine("SnapCat AI foundation checks passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"SnapCat AI foundation checks failed: {exception.Message}");
    return 1;
}
finally
{
    if (Directory.Exists(temporaryDirectory))
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }
}

static async Task VerifyAiProfilePersistenceAsync(string directory)
{
    const string apiKey = "snapcat-foundation-check-secret";
    var store = new JsonSettingsStore(directory);
    var settings = new AppSettings
    {
        AutoCheckUpdates = false,
        AiProviderProfiles =
        [
            new AiProviderProfile
            {
                Name = "本地测试视觉配置",
                Protocol = AiProviderProtocol.OpenAiCompatible,
                BaseUrl = "https://example.invalid/v1",
                ApiKey = apiKey,
                Model = "vision-test-model",
                Capabilities = AiModelCapabilities.VisionAnalysis
                    | AiModelCapabilities.TextToImage
                    | AiModelCapabilities.MultipleReferenceImages,
                MaxReferenceImageCount = 4,
                MaxOutputCount = 6,
                SupportsCostEstimate = true
            },
        ]
    };
    settings.NormalizeAiProviderProfiles();

    await store.SaveAsync(settings);
    var persistedSettings = await File.ReadAllTextAsync(Path.Combine(directory, "settings.json"));
    Assert(!persistedSettings.Contains(apiKey, StringComparison.Ordinal), "AI API Key must not be persisted as plaintext.");

    var loaded = await store.LoadAsync();
    var profile = loaded.GetSelectedAiProviderProfile()
        ?? throw new InvalidOperationException("Saved AI provider profile should be restored.");
    Assert(profile.ApiKey == apiKey, "DPAPI-protected AI API Key should be restored.");
    Assert(profile.Supports(AiModelCapabilities.VisionAnalysis | AiModelCapabilities.TextToImage), "Saved AI capabilities should be restored.");
    Assert(profile.MaxReferenceImageCount == 4 && profile.MaxOutputCount == 6, "Saved AI limits should be restored.");
    Assert(!loaded.AutoCheckUpdates, "The automatic update preference should be restored.");
}

static async Task VerifySettingsBackupRecoveryAsync(string directory)
{
    var settingsDirectory = Path.Combine(directory, "settings-backup");
    var store = new JsonSettingsStore(settingsDirectory);
    var baseline = new AppSettings
    {
        ThemeId = "ocean-blue",
        TargetLanguage = "zh-CN",
        AutoCheckUpdates = true
    };
    await store.SaveAsync(baseline);

    var replacement = AppSettingsCloneService.Clone(baseline);
    replacement.ThemeId = "forest-green";
    replacement.AutoCheckUpdates = false;
    await store.SaveAsync(replacement);

    await File.WriteAllTextAsync(Path.Combine(settingsDirectory, "settings.json"), "{ this is not valid json }");
    var restored = await store.LoadAsync();
    Assert(restored.ThemeId == "ocean-blue", "A corrupted primary settings file should recover from the last verified backup.");
    Assert(restored.AutoCheckUpdates, "The recovered backup should preserve user update preferences.");
}

static async Task VerifyImageGenerationProfilePersistenceAsync(string directory)
{
    const string apiKey = "snapcat-comfy-secret";
    var settingsDirectory = Path.Combine(directory, "image-generation-profile");
    var store = new JsonSettingsStore(settingsDirectory);
    var settings = new AppSettings
    {
        ImageGenerationProfiles =
        [
            new ImageGenerationProfile
            {
                Id = "comfy-local",
                Name = "本地 ComfyUI",
                BaseUrl = "http://127.0.0.1:8188",
                ApiKey = apiKey,
                DefaultCheckpoint = "sdxl-base.safetensors",
                IsDefault = true,
                DefaultWidth = 1216,
                DefaultHeight = 832,
                DefaultSteps = 28,
                DefaultCfgScale = 6.5d
            }
        ],
        SelectedImageGenerationProfileId = "comfy-local"
    };
    settings.NormalizeImageGenerationProfiles();

    await store.SaveAsync(settings);
    var persisted = await File.ReadAllTextAsync(Path.Combine(settingsDirectory, "settings.json"));
    Assert(!persisted.Contains(apiKey, StringComparison.Ordinal), "Image generation credentials must not be persisted as plaintext.");

    var loaded = await store.LoadAsync();
    var profile = loaded.GetSelectedImageGenerationProfile()
        ?? throw new InvalidOperationException("Image generation profile should be restored.");
    Assert(profile.ApiKey == apiKey, "DPAPI-protected generation credentials should be restored.");
    Assert(profile.DefaultCheckpoint == "sdxl-base.safetensors", "The selected checkpoint should be restored.");
    Assert(profile.DefaultWidth == 1216 && profile.DefaultHeight == 832, "Image generation dimensions should be restored.");
    Assert(profile.DefaultSteps == 28 && Math.Abs(profile.DefaultCfgScale - 6.5d) < 0.001d, "Image generation sampling parameters should be restored.");
}

static async Task VerifyProjectWorkspaceAsync(string directory)
{
    var userDataDirectory = Path.Combine(directory, "project-workspace-user-data");
    var sourceDirectory = Path.Combine(directory, "source-images");
    Directory.CreateDirectory(sourceDirectory);
    var sourcePath = Path.Combine(sourceDirectory, "reference.png");
    await File.WriteAllBytesAsync(
        sourcePath,
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Jd4UAAAAASUVORK5CYII="));

    var service = new ProjectWorkspaceService(userDataDirectory);
    var workspace = await service.CreateAsync(service.DefaultProjectsDirectory, "基础项目");
    Assert(File.Exists(Path.Combine(workspace.DirectoryPath, "project.json")), "New projects should write project.json.");
    Assert(Directory.Exists(Path.Combine(workspace.DirectoryPath, "originals")), "New projects should initialize the originals directory.");
    Assert(Directory.Exists(Path.Combine(workspace.DirectoryPath, "thumbnails")), "New projects should initialize the thumbnails directory.");

    var asset = await service.ImportImageAsync(
        workspace,
        sourcePath,
        ProjectAssetKind.Reference,
        ProjectAssetCategory.StyleReference);
    Assert(!string.IsNullOrWhiteSpace(asset.Id), "Imported project assets need a stable ID.");
    Assert(!Path.IsPathRooted(asset.RelativePath), "Project asset paths must stay relative for portable projects.");
    Assert(File.Exists(Path.Combine(workspace.DirectoryPath, asset.RelativePath)), "Importing should copy the source into the project.");
    Assert(File.Exists(sourcePath), "Importing must not move or delete the source image.");
    var references = ProjectAssetReferenceResolver.Resolve(workspace.Project, "使用 @reference 与 @{reference.png} 作为参考。");
    Assert(references.Count == 1 && references[0].Asset.Id == asset.Id, "Project prompt references should resolve explicit material names without filesystem paths.");

    var derivedAsset = await service.CreateDerivedAssetAsync(workspace, asset.Id, sourcePath);
    Assert(derivedAsset.ParentAssetId == asset.Id && derivedAsset.Version == asset.Version + 1, "Derived project assets should retain a stable parent reference and increment version.");

    var collection = await service.CreateCollectionAsync(workspace, "角色参考", [asset.Id, derivedAsset.Id]);
    Assert(collection.AssetIds.SequenceEqual([asset.Id, derivedAsset.Id]), "New project collections should retain stable asset IDs.");

    var backupDirectory = Path.Combine(directory, "project-backups");
    var backupPath = await service.CreateBackupAsync(workspace, backupDirectory);
    Assert(File.Exists(backupPath), "Project backups should create a ZIP file.");
    using (var backup = ZipFile.OpenRead(backupPath))
    {
        Assert(backup.Entries.Any(entry => entry.FullName == "project.json"), "Project backups must include project metadata.");
        Assert(
            backup.Entries.Any(entry => string.Equals(
                entry.FullName.Replace('\\', '/'),
                asset.RelativePath.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase)),
            "Project backups must include imported assets.");
    }

    var movedProjectDirectory = Path.Combine(directory, "moved-project");
    Directory.Move(workspace.DirectoryPath, movedProjectDirectory);
    var reopened = await service.OpenAsync(movedProjectDirectory);
    Assert(reopened.Project.Assets.Any(item => item.Id == asset.Id), "Reopening a project should preserve imported asset IDs.");
    Assert(reopened.Project.Assets.Single(item => item.Id == derivedAsset.Id).ParentAssetId == asset.Id, "Reopening a project should preserve derived asset relationships.");
    Assert(reopened.Project.Collections.Single().AssetIds.Count == 2, "Reopening a project should preserve collection asset references.");
    Assert(File.Exists(Path.Combine(reopened.DirectoryPath, asset.RelativePath)), "Moving a project directory should preserve relative asset references.");
    Assert(await service.GetLastOpenedProjectDirectoryAsync() == movedProjectDirectory, "The last opened project should be stored in user-local state.");

    var recycledCount = await service.MoveToRecycleBinAsync(reopened, [asset.Id]);
    Assert(recycledCount == 1, "Deleting a project asset should move exactly the selected asset to the project recycle bin.");
    Assert(reopened.Project.Assets.Single().Id == derivedAsset.Id, "Recycling one asset should preserve unrelated derived assets.");
    Assert(reopened.Project.Collections.Single().AssetIds.Single() == derivedAsset.Id, "Recycled assets must be removed from project collection references.");
    Assert(Directory.EnumerateFiles(Path.Combine(reopened.DirectoryPath, "recycle-bin")).Any(), "The project recycle bin should retain moved asset data.");
    Assert((await service.GetRecycledAssetsAsync(reopened)).Single().Id == asset.Id, "Recycle-bin metadata should be readable for restore UI.");

    var restoredCount = await service.RestoreFromRecycleBinAsync(reopened, [asset.Id]);
    Assert(restoredCount == 1, "Project recycle-bin assets should restore by their stable ID.");
    Assert(reopened.Project.Assets.Any(item => item.Id == asset.Id), "Restored project assets should retain their original stable ID.");
    Assert(reopened.Project.Collections.Single().AssetIds.SequenceEqual([derivedAsset.Id, asset.Id]), "Restoring an asset should restore its previous project collection membership.");
    Assert(File.Exists(Path.Combine(reopened.DirectoryPath, asset.RelativePath)), "Restoring an asset should recover its original project-relative file.");

}

static void VerifySettingsSnapshotAndComparison()
{
    var original = new AppSettings
    {
        HotkeyCaptureAndCopy = "Control+Shift+Oem3",
        TrayTooltipWorkflowOne = "CaptureAndVisualPrompt",
        AutoCheckUpdates = false,
        AiProviderProfiles =
        [
            new AiProviderProfile
            {
                Id = "vision-profile",
                Name = "Local vision",
                Protocol = AiProviderProtocol.Ollama,
                BaseUrl = "http://127.0.0.1:11434",
                Model = "qwen3-vl:8b",
                Capabilities = AiModelCapabilities.VisionAnalysis,
                MaxReferenceImageCount = 3,
                MaxOutputCount = 2,
                SupportsCostEstimate = false
            },
            new AiProviderProfile
            {
                Id = "vision-profile-secondary",
                Name = "Local vision secondary",
                Protocol = AiProviderProtocol.Ollama,
                BaseUrl = "http://127.0.0.1:11434",
                Model = "qwen3-vl:8b-alt",
                Capabilities = AiModelCapabilities.VisionAnalysis
            }
        ]
    };
    original.NormalizeAiProviderProfiles();
    var snapshot = AppSettingsCloneService.Clone(original);
    Assert(AppSettingsComparer.AreEquivalent(original, snapshot), "A detached settings snapshot should initially match its source.");
    Assert(HotkeyTextNormalizer.Normalize(snapshot.HotkeyCaptureAndCopy) == "Ctrl+Shift+`", "Persisted OEM hotkeys should use readable keyboard symbols.");

    snapshot.AiProviderProfiles[0].MaxOutputCount = 4;
    Assert(!AppSettingsComparer.AreEquivalent(original, snapshot), "AI output limits must participate in the unsaved-settings comparison.");
    Assert(original.AiProviderProfiles[0].MaxOutputCount == 2, "Changing a settings snapshot must not mutate the live profile collection.");

    snapshot = AppSettingsCloneService.Clone(original);
    snapshot.AutoCheckUpdates = true;
    Assert(!AppSettingsComparer.AreEquivalent(original, snapshot), "Automatic update changes must participate in the unsaved-settings comparison.");

    snapshot = AppSettingsCloneService.Clone(original);
    snapshot.ImageGenerationProfiles.Add(new ImageGenerationProfile
    {
        Id = "comfy-local",
        Name = "ComfyUI",
        DefaultCheckpoint = "sdxl.safetensors"
    });
    snapshot.SelectedImageGenerationProfileId = "comfy-local";
    Assert(!AppSettingsComparer.AreEquivalent(original, snapshot), "Image generation profiles must participate in the unsaved-settings comparison.");
}

static void VerifyTaskStateMachine()
{
    var coordinator = new AiTaskCoordinator();
    var task = coordinator.Create(new AiTaskRequest
    {
        Kind = AiTaskKind.ImageGeneration,
        DisplayName = "基础状态机测试",
        OutputCount = 1
    });

    Assert(task.Status == AiTaskStatus.Ready, "New tasks should start ready.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Queued), "Ready tasks should queue.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Running), "Queued tasks should run.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Failed, "测试失败"), "Running tasks should fail with a diagnostic.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Queued), "Failed tasks should retry from the queue.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Running), "Retried tasks should run.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Cancelling), "Running tasks should enter cancelling.");
    Assert(coordinator.TryTransition(task.Id, AiTaskStatus.Cancelled), "Cancelling tasks should become cancelled.");

    var activeTask = coordinator.Create(new AiTaskRequest { Kind = AiTaskKind.VisionAnalysis });
    var interruptedCount = coordinator.InterruptActiveTasks("测试退出");
    Assert(interruptedCount == 1, "Only active tasks should be interrupted.");
    Assert(coordinator.Get(activeTask.Id)?.Status == AiTaskStatus.Interrupted, "Active tasks should be marked interrupted.");
}

static async Task VerifyComfyUiGenerationAsync()
{
    var handler = new ComfyUiHttpMessageHandler();
    using var client = new HttpClient(handler);
    var service = new ComfyUiImageGenerationService(client, new AiTaskCoordinator());
    var profile = new ImageGenerationProfile
    {
        Id = "comfy-local",
        Name = "ComfyUI",
        BaseUrl = "http://127.0.0.1:8188",
        DefaultCheckpoint = "sdxl-base.safetensors"
    };

    var connection = await service.TestConnectionAsync(profile);
    Assert(connection.Success, "ComfyUI system stats should validate the backend connection.");

    var models = await service.GetCheckpointModelsAsync(profile);
    Assert(models.SequenceEqual(["sdxl-base.safetensors", "sdxl-refiner.safetensors"]), "ComfyUI checkpoint discovery should return stable sorted model names.");

    var result = await service.GenerateAsync(new ImageGenerationRequest
    {
        Prompt = "a blue cat on a desk",
        NegativePrompt = "low quality",
        Width = 1025,
        Height = 769,
        Steps = 20,
        CfgScale = 6d
    }, profile);
    Assert(result.Success && result.Outputs.Count == 1, "ComfyUI single-image generation should return its output artifact.");
    Assert(result.Outputs[0].Content.SequenceEqual(new byte[] { 1, 2, 3 }), "ComfyUI output bytes should be downloaded through the view endpoint.");
    Assert(handler.LastPromptPayload.Contains("CheckpointLoaderSimple", StringComparison.Ordinal), "The ComfyUI workflow should include a checkpoint loader.");
    Assert(handler.LastPromptPayload.Contains("CLIPTextEncode", StringComparison.Ordinal), "The ComfyUI workflow should include prompt encoders.");
    Assert(handler.LastPromptPayload.Contains("\"width\":1024", StringComparison.Ordinal), "Workflow dimensions should normalize to multiples of eight.");

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var cancelled = await service.GenerateAsync(new ImageGenerationRequest
    {
        Prompt = "cancelled generation"
    }, profile, cancellation.Token);
    Assert(!cancelled.Success && cancelled.ErrorMessage.Contains("停止等待", StringComparison.Ordinal), "Cancelling generation should return an explicit local-wait status.");
}

static void VerifyReleaseUpdateManifest()
{
    Assert(ReleaseVersionComparer.IsNewer("0.4.1-preview", "0.4.0-preview"), "A newer preview version should be detected.");
    Assert(ReleaseVersionComparer.IsNewer("0.4.1", "0.4.1-preview"), "A stable version should be newer than its preview.");
    Assert(!ReleaseVersionComparer.IsNewer("0.4.0-preview", "0.4.0-preview"), "The current version must not update itself.");

    const string sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    var manifestJson = $$"""
    {
      "version": "0.4.1-preview",
      "channel": "preview",
      "publishedAt": "2026-07-11T00:00:00+00:00",
      "packages": [
        {
          "kind": "portable",
          "downloadUrl": "https://example.invalid/SnapCat-v0.4.1-preview-win-x64-portable.zip",
          "sha256": "{{sha256}}",
          "sizeBytes": 1024
        }
      ]
    }
    """;
    var manifest = new ReleaseUpdateManifestService().Parse(manifestJson);
    Assert(manifest.GetPackage(ReleasePackageKind.Portable) is not null, "The portable package should be parsed from the manifest.");

    try
    {
        new ReleaseUpdateManifestService().Parse(manifestJson.Replace(sha256, "invalid", StringComparison.Ordinal));
        throw new InvalidOperationException("Invalid SHA256 values must be rejected.");
    }
    catch (InvalidDataException)
    {
        // Expected validation failure.
    }
}

static async Task VerifyDownloadedUpdatePackageStagingAsync(string directory)
{
    var packageBytes = CreateUpdateArchive();
    var workingDirectory = Path.Combine(directory, "download-stage");
    using var client = new HttpClient(new UpdatePackageHttpMessageHandler(packageBytes));
    var service = new ReleaseUpdatePackageService(client);
    var package = new ReleasePackageManifest
    {
        Kind = ReleasePackageKind.Portable,
        DownloadUrl = "https://example.invalid/SnapCat-update.zip",
        Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(packageBytes)).ToLowerInvariant(),
        SizeBytes = packageBytes.Length
    };

    var stages = new List<ReleaseUpdateProgressStage>();
    var staged = await service.DownloadAndStageAsync(
        package,
        workingDirectory,
        stageProgress: new InlineProgress<ReleaseUpdateProgress>(progress => stages.Add(progress.Stage)));
    Assert(File.Exists(staged.ArchivePath), "Downloaded update archives should be moved after their write stream is released.");
    Assert(File.Exists(Path.Combine(staged.StagingDirectory, "SnapCat.exe")), "Downloaded updates should stage a runnable application payload.");
    Assert(stages.Contains(ReleaseUpdateProgressStage.Downloading), "Update downloads should report the download stage.");
    Assert(stages.Contains(ReleaseUpdateProgressStage.Verifying), "Update downloads should report the verification stage.");
    Assert(stages.Contains(ReleaseUpdateProgressStage.Extracting), "Update downloads should report the extraction stage.");
    Assert(stages.LastOrDefault() == ReleaseUpdateProgressStage.Ready, "A staged update should report the ready stage last.");
}

static byte[] CreateUpdateArchive()
{
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        var executable = archive.CreateEntry("SnapCat.exe");
        using var writer = new StreamWriter(executable.Open());
        writer.Write("test payload");
    }

    return stream.ToArray();
}

static async Task VerifyUpdatePackageStagingAsync(string directory)
{
    var updateDirectory = Path.Combine(directory, "update-package");
    Directory.CreateDirectory(updateDirectory);
    var archivePath = Path.Combine(updateDirectory, "update.zip");
    using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
    {
        await WriteZipEntryAsync(archive, "SnapCat.exe", "test executable");
        await WriteZipEntryAsync(archive, "README.md", "test readme");
    }

    var service = new ReleaseUpdatePackageService(new HttpClient());
    var archiveSize = new FileInfo(archivePath).Length;
    var package = new ReleasePackageManifest
    {
        Kind = ReleasePackageKind.Portable,
        DownloadUrl = "https://example.invalid/SnapCat-update.zip",
        Sha256 = await ReleaseUpdatePackageService.ComputeSha256Async(archivePath),
        SizeBytes = archiveSize
    };
    var staged = await service.StageArchiveAsync(archivePath, package, updateDirectory);
    Assert(File.Exists(Path.Combine(staged.StagingDirectory, "SnapCat.exe")), "A verified update package should stage SnapCat.exe.");

    var unsafeArchivePath = Path.Combine(updateDirectory, "unsafe.zip");
    using (var archive = ZipFile.Open(unsafeArchivePath, ZipArchiveMode.Create))
    {
        await WriteZipEntryAsync(archive, "../outside.txt", "unsafe");
    }

    var unsafePackage = new ReleasePackageManifest
    {
        Kind = ReleasePackageKind.Portable,
        DownloadUrl = "https://example.invalid/unsafe.zip",
        Sha256 = await ReleaseUpdatePackageService.ComputeSha256Async(unsafeArchivePath),
        SizeBytes = new FileInfo(unsafeArchivePath).Length
    };
    try
    {
        await service.StageArchiveAsync(unsafeArchivePath, unsafePackage, updateDirectory);
        throw new InvalidOperationException("Unsafe update archive paths must be rejected.");
    }
    catch (InvalidDataException)
    {
        // Expected validation failure.
    }
}

static async Task WriteZipEntryAsync(ZipArchive archive, string entryName, string content)
{
    var entry = archive.CreateEntry(entryName);
    await using var stream = entry.Open();
    await using var writer = new StreamWriter(stream);
    await writer.WriteAsync(content);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class UpdatePackageHttpMessageHandler(byte[] packageBytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(packageBytes)
        });
    }
}

sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

sealed class ComfyUiHttpMessageHandler : HttpMessageHandler
{
    public string LastPromptPayload { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (request.Method == HttpMethod.Get && path.EndsWith("/system_stats", StringComparison.Ordinal))
        {
            return Json("{\"system\":{\"os\":\"Windows\"}}");
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/object_info/CheckpointLoaderSimple", StringComparison.Ordinal))
        {
            return Json("{\"CheckpointLoaderSimple\":{\"input\":{\"required\":{\"ckpt_name\":[[\"sdxl-refiner.safetensors\",\"sdxl-base.safetensors\"]]}}}}");
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/prompt", StringComparison.Ordinal))
        {
            LastPromptPayload = await request.Content!.ReadAsStringAsync(cancellationToken);
            return Json("{\"prompt_id\":\"prompt-1\"}");
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/history/prompt-1", StringComparison.Ordinal))
        {
            return Json("{\"prompt-1\":{\"outputs\":{\"7\":{\"images\":[{\"filename\":\"SnapCat_00001.png\",\"subfolder\":\"\",\"type\":\"output\"}]}}}}");
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/view", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string content) => new(System.Net.HttpStatusCode.OK)
    {
        Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
    };
}
