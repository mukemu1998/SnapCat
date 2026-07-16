using System.Net.Http.Json;
using System.Text.Json;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

/// <summary>
/// Minimal ComfyUI adapter. It deliberately uses the stable prompt/history/view API surface
/// so later canvas and node editors can share the same task and artifact pipeline.
/// </summary>
public sealed class ComfyUiImageGenerationService : IImageGenerationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan GenerationTimeout = TimeSpan.FromMinutes(8);

    private readonly HttpClient _httpClient;
    private readonly IAiTaskCoordinator _taskCoordinator;

    public ComfyUiImageGenerationService(HttpClient httpClient, IAiTaskCoordinator taskCoordinator)
    {
        _httpClient = httpClient;
        _taskCoordinator = taskCoordinator;
    }

    public async Task<ImageGenerationConnectionResult> TestConnectionAsync(
        ImageGenerationProfile profile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureComfyUiProfile(profile);
            using var response = await _httpClient.GetAsync(BuildEndpoint(profile.BaseUrl, "system_stats"), cancellationToken);
            response.EnsureSuccessStatusCode();
            return new ImageGenerationConnectionResult { Success = true, Message = "ComfyUI 连接正常。" };
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            return new ImageGenerationConnectionResult { Message = $"无法连接 ComfyUI：{exception.Message}" };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ImageGenerationConnectionResult { Message = "连接 ComfyUI 超时，请确认服务已启动且接口地址正确。" };
        }
    }

    public async Task<IReadOnlyList<string>> GetCheckpointModelsAsync(
        ImageGenerationProfile profile,
        CancellationToken cancellationToken = default)
    {
        EnsureComfyUiProfile(profile);
        using var response = await _httpClient.GetAsync(BuildEndpoint(profile.BaseUrl, "object_info/CheckpointLoaderSimple"), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("CheckpointLoaderSimple", out var node)
            || !node.TryGetProperty("input", out var input)
            || !input.TryGetProperty("required", out var required)
            || !required.TryGetProperty("ckpt_name", out var checkpointInput)
            || checkpointInput.ValueKind != JsonValueKind.Array
            || checkpointInput.GetArrayLength() == 0
            || checkpointInput[0].ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return checkpointInput[0]
            .EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureComfyUiProfile(profile);

        if (!profile.IsEnabled)
        {
            return ImageGenerationResult.FromError("当前生图配置未启用。", providerProfileId: profile.Id);
        }

        var checkpoint = string.IsNullOrWhiteSpace(request.Checkpoint)
            ? profile.DefaultCheckpoint
            : request.Checkpoint.Trim();
        if (string.IsNullOrWhiteSpace(checkpoint))
        {
            return ImageGenerationResult.FromError("请先选择 ComfyUI 已安装的基础模型。", providerProfileId: profile.Id);
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return ImageGenerationResult.FromError("请输入用于生图的提示词。", providerProfileId: profile.Id);
        }

        var task = _taskCoordinator.Create(new AiTaskRequest
        {
            Kind = AiTaskKind.ImageGeneration,
            DisplayName = "ComfyUI 单图文生图",
            ProviderProfileId = profile.Id,
            ModelName = checkpoint,
            OutputCount = Math.Clamp(request.OutputCount, 1, 16)
        });
        _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Queued);
        _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Running);

        try
        {
            var clientId = Guid.NewGuid().ToString("N");
            var workflow = ComfyUiWorkflowFactory.CreateTextToImageWorkflow(request, profile, checkpoint);
            using var enqueueResponse = await _httpClient.PostAsJsonAsync(
                BuildEndpoint(profile.BaseUrl, "prompt"),
                new { prompt = workflow, client_id = clientId },
                cancellationToken);
            var enqueuePayload = await enqueueResponse.Content.ReadAsStringAsync(cancellationToken);
            enqueueResponse.EnsureSuccessStatusCode();
            var remoteTaskId = ReadPromptId(enqueuePayload);

            var imageReferences = await WaitForOutputsAsync(profile, remoteTaskId, cancellationToken);
            var outputs = new List<ImageGenerationOutput>(imageReferences.Count);
            foreach (var image in imageReferences)
            {
                using var imageResponse = await _httpClient.GetAsync(BuildViewEndpoint(profile.BaseUrl, image), cancellationToken);
                imageResponse.EnsureSuccessStatusCode();
                outputs.Add(new ImageGenerationOutput
                {
                    FileName = image.FileName,
                    Subfolder = image.Subfolder,
                    Type = image.Type,
                    Content = await imageResponse.Content.ReadAsByteArrayAsync(cancellationToken)
                });
            }

            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Succeeded);
            return new ImageGenerationResult
            {
                Success = true,
                TaskId = task.Id,
                ProviderProfileId = profile.Id,
                RemoteTaskId = remoteTaskId,
                Outputs = outputs
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Cancelling);
            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Cancelled);
            return ImageGenerationResult.FromError(
                "SnapCat 已停止等待；若 ComfyUI 已开始执行，任务可能继续在本地队列中运行。",
                task.Id,
                profile.Id);
        }
        catch (Exception exception)
        {
            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Failed, exception.Message);
            return ImageGenerationResult.FromError($"ComfyUI 生图失败：{exception.Message}", task.Id, profile.Id);
        }
    }

    private async Task<IReadOnlyList<ComfyUiImageReference>> WaitForOutputsAsync(
        ImageGenerationProfile profile,
        string remoteTaskId,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < GenerationTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _httpClient.GetAsync(BuildEndpoint(profile.BaseUrl, $"history/{Uri.EscapeDataString(remoteTaskId)}"), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var outputs = ParseHistoryOutputs(payload, remoteTaskId);
            if (outputs.Count > 0)
            {
                return outputs;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new TimeoutException("等待 ComfyUI 返回结果超时，请检查本地队列或模型状态。");
    }

    private static IReadOnlyList<ComfyUiImageReference> ParseHistoryOutputs(string payload, string remoteTaskId)
    {
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty(remoteTaskId, out var history)
            || !history.TryGetProperty("outputs", out var outputs)
            || outputs.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var images = new List<ComfyUiImageReference>();
        foreach (var output in outputs.EnumerateObject())
        {
            if (!output.Value.TryGetProperty("images", out var outputImages)
                || outputImages.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var image in outputImages.EnumerateArray())
            {
                if (!image.TryGetProperty("filename", out var filename))
                {
                    continue;
                }

                var value = filename.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                images.Add(new ComfyUiImageReference(
                    value,
                    image.TryGetProperty("subfolder", out var subfolder) ? subfolder.GetString()?.Trim() ?? string.Empty : string.Empty,
                    image.TryGetProperty("type", out var type) ? type.GetString()?.Trim() ?? "output" : "output"));
            }
        }

        return images;
    }

    private static string ReadPromptId(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("prompt_id", out var promptId)
            && !string.IsNullOrWhiteSpace(promptId.GetString()))
        {
            return promptId.GetString()!.Trim();
        }

        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"ComfyUI 拒绝了工作流：{error}");
        }

        throw new InvalidOperationException("ComfyUI 未返回任务编号。");
    }

    private static void EnsureComfyUiProfile(ImageGenerationProfile? profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (!string.Equals(ImageGenerationProtocol.Normalize(profile.Protocol), ImageGenerationProtocol.ComfyUi, StringComparison.Ordinal))
        {
            throw new NotSupportedException("当前仅支持 ComfyUI 本地生图后端。");
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl))
        {
            throw new InvalidOperationException("请先填写 ComfyUI 接口地址。");
        }
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath)
    {
        var normalized = baseUrl.Trim().TrimEnd('/') + "/";
        return new Uri(new Uri(normalized, UriKind.Absolute), relativePath);
    }

    private static Uri BuildViewEndpoint(string baseUrl, ComfyUiImageReference image)
    {
        var endpoint = BuildEndpoint(baseUrl, "view");
        var query = $"filename={Uri.EscapeDataString(image.FileName)}&subfolder={Uri.EscapeDataString(image.Subfolder)}&type={Uri.EscapeDataString(image.Type)}";
        return new Uri($"{endpoint}?{query}", UriKind.Absolute);
    }

    private sealed record ComfyUiImageReference(string FileName, string Subfolder, string Type);
}

internal static class ComfyUiWorkflowFactory
{
    public static Dictionary<string, object> CreateTextToImageWorkflow(
        ImageGenerationRequest request,
        ImageGenerationProfile profile,
        string checkpoint)
    {
        var width = NormalizeDimension(request.Width, profile.DefaultWidth);
        var height = NormalizeDimension(request.Height, profile.DefaultHeight);
        var steps = Math.Clamp(request.Steps <= 0 ? profile.DefaultSteps : request.Steps, 1, 150);
        var cfgScale = Math.Clamp(request.CfgScale <= 0 ? profile.DefaultCfgScale : request.CfgScale, 1d, 30d);
        var batchSize = Math.Clamp(request.OutputCount, 1, 16);

        return new Dictionary<string, object>
        {
            ["1"] = new { class_type = "CheckpointLoaderSimple", inputs = new { ckpt_name = checkpoint } },
            ["2"] = new { class_type = "CLIPTextEncode", inputs = new { text = request.Prompt.Trim(), clip = new object[] { "1", 1 } } },
            ["3"] = new { class_type = "CLIPTextEncode", inputs = new { text = request.NegativePrompt?.Trim() ?? string.Empty, clip = new object[] { "1", 1 } } },
            ["4"] = new { class_type = "EmptyLatentImage", inputs = new { width, height, batch_size = batchSize } },
            ["5"] = new
            {
                class_type = "KSampler",
                inputs = new
                {
                    seed = request.Seed < 0 ? Random.Shared.NextInt64(long.MaxValue) : request.Seed,
                    steps,
                    cfg = cfgScale,
                    sampler_name = "euler",
                    scheduler = "normal",
                    denoise = 1d,
                    model = new object[] { "1", 0 },
                    positive = new object[] { "2", 0 },
                    negative = new object[] { "3", 0 },
                    latent_image = new object[] { "4", 0 }
                }
            },
            ["6"] = new { class_type = "VAEDecode", inputs = new { samples = new object[] { "5", 0 }, vae = new object[] { "1", 2 } } },
            ["7"] = new { class_type = "SaveImage", inputs = new { filename_prefix = "SnapCat", images = new object[] { "6", 0 } } }
        };
    }

    private static int NormalizeDimension(int value, int fallback)
    {
        var normalized = value <= 0 ? fallback : Math.Clamp(value, 256, 4096);
        return normalized - (normalized % 8);
    }
}
