using System.Net.Http.Json;
using System.Text.Json;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class SmartVisualPromptService : IVisualPromptService
{
    private const string AnalysisInstruction = """
你是专业的视觉设计分析助手。请分析输入图片，并且只返回一个 JSON 对象，不要 Markdown，不要解释。
JSON 必须使用以下字段：
{
  "subject": "主体、角色或物件",
  "appearance": "外观、服装、材质、配饰和关键细节",
  "composition": "构图、镜头、视角、姿势或画面关系",
  "lighting": "光线、色彩、氛围",
  "style": "画风、渲染和质量特征",
  "subjectEn": "英文主体描述",
  "appearanceEn": "英文外观、服装、材质、配饰和关键细节",
  "compositionEn": "英文构图、镜头、视角、姿势或画面关系",
  "lightingEn": "英文光线、色彩、氛围",
  "styleEn": "英文画风、渲染和质量特征",
  "analysisEn": "完整英文分析，覆盖主体、外观、构图、光影与风格",
  "promptZh": "可直接用于生图的中文提示词",
  "promptEn": "可直接用于生图的英文提示词",
  "negativePrompt": "建议的负面提示词",
  "negativePromptEn": "英文负面提示词"
}
图片不清晰时请如实说明，不要编造无法确认的细节。
""";

    private readonly HttpClient _httpClient;
    private readonly IAiTaskCoordinator _taskCoordinator;

    public SmartVisualPromptService(HttpClient httpClient, IAiTaskCoordinator taskCoordinator)
    {
        _httpClient = httpClient;
        _taskCoordinator = taskCoordinator;
    }

    public async Task<VisualPromptResult> AnalyzeAsync(
        string imagePath,
        AiProviderProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return VisualPromptResult.FromError("视觉分析失败：图片文件不存在。", providerProfileId: profile?.Id ?? string.Empty);
        }

        if (profile is null || !profile.IsEnabled)
        {
            return VisualPromptResult.FromError("请先在视觉分析设置中选择已启用的 AI 配置。");
        }

        profile.Normalize(0);
        if (!profile.Supports(AiModelCapabilities.VisionAnalysis))
        {
            return VisualPromptResult.FromError("当前 AI 配置未声明“视觉分析”能力。", providerProfileId: profile.Id);
        }

        if (string.IsNullOrWhiteSpace(profile.BaseUrl) || string.IsNullOrWhiteSpace(profile.Model))
        {
            return VisualPromptResult.FromError("请完整填写视觉分析配置的接口地址和模型名称。", providerProfileId: profile.Id);
        }

        var task = _taskCoordinator.Create(new AiTaskRequest
        {
            Kind = AiTaskKind.VisionAnalysis,
            DisplayName = "图片提示词分析",
            ProviderProfileId = profile.Id,
            ModelName = profile.Model,
            ReferenceImageCount = 1,
            OutputCount = 1
        });
        _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Queued);
        _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Running);

        try
        {
            var response = AiProviderProtocol.Normalize(profile.Protocol) switch
            {
                AiProviderProtocol.Ollama => await AnalyzeWithOllamaAsync(imagePath, profile, cancellationToken),
                AiProviderProtocol.OpenAiCompatible or AiProviderProtocol.Custom => await AnalyzeWithOpenAiCompatibleAsync(imagePath, profile, cancellationToken),
                AiProviderProtocol.Gemini => throw new NotSupportedException("Gemini 原生协议将在后续适配版本开放；当前可使用 OpenAI 兼容接口或 Ollama。"),
                _ => throw new NotSupportedException("当前 AI 配置协议不受支持。")
            };

            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Succeeded);
            return new VisualPromptResult
            {
                Success = true,
                TaskId = task.Id,
                ProviderProfileId = profile.Id,
                RawResponse = response,
                Analysis = VisualPromptResponseParser.Parse(response)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Cancelling);
            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Cancelled);
            return VisualPromptResult.FromError("视觉分析已取消。", task.Id, profile.Id);
        }
        catch (Exception exception)
        {
            _taskCoordinator.TryTransition(task.Id, AiTaskStatus.Failed, exception.Message);
            return VisualPromptResult.FromError($"视觉分析失败：{exception.Message}", task.Id, profile.Id);
        }
    }

    private async Task<string> AnalyzeWithOllamaAsync(string imagePath, AiProviderProfile profile, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(profile.BaseUrl, "api/chat");
        var imageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(imagePath, cancellationToken));
        var request = new
        {
            model = profile.Model,
            stream = false,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = AnalysisInstruction,
                    images = new[] { imageBase64 }
                }
            },
            options = new { temperature = 0.2 }
        };

        using var response = await _httpClient.PostAsJsonAsync(endpoint, request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString()?.Trim() ?? string.Empty;
        }

        throw new InvalidOperationException("Ollama 未返回可用的分析内容。");
    }

    private async Task<string> AnalyzeWithOpenAiCompatibleAsync(string imagePath, AiProviderProfile profile, CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(profile.BaseUrl, "chat/completions");
        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var dataUrl = $"data:{GetImageMimeType(imagePath)};base64,{Convert.ToBase64String(imageBytes)}";
        var request = new
        {
            model = profile.Model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = AnalysisInstruction },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "请按约定 JSON 格式完成图片提示词分析。" },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(request)
        };
        if (!string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", profile.ApiKey);
        }

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var messageElement = choices[0].GetProperty("message");
            if (messageElement.TryGetProperty("content", out var content))
            {
                return content.ValueKind == JsonValueKind.String
                    ? content.GetString()?.Trim() ?? string.Empty
                    : content.ToString().Trim();
            }
        }

        throw new InvalidOperationException("兼容视觉接口未返回可用的分析内容。");
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath)
    {
        var normalized = baseUrl.TrimEnd('/') + "/";
        if (normalized.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase))
        {
            return new Uri(normalized, UriKind.Absolute);
        }

        return new Uri(new Uri(normalized, UriKind.Absolute), relativePath);
    }

    private static string GetImageMimeType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/png"
        };
    }
}
