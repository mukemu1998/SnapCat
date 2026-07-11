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
    await VerifySettingsBackupRecoveryAsync(temporaryDirectory);
    VerifySettingsSnapshotAndComparison();
    VerifyReleaseUpdateManifest();
    await VerifyUpdatePackageStagingAsync(temporaryDirectory);
    VerifyTaskStateMachine();
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
