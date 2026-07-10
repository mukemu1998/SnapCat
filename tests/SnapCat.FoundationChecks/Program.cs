using SnapCat.Core.Models;
using SnapCat.Infrastructure.Services;

var temporaryDirectory = Path.Combine(
    Path.GetTempPath(),
    "SnapCat.FoundationChecks",
    Guid.NewGuid().ToString("N"));

try
{
    await VerifyAiProfilePersistenceAsync(temporaryDirectory);
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
            }
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

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
