using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class OpenAiCompatibleTranslationService : ITranslationService
{
    // Most OpenAI-compatible providers accept several thousand characters per request.
    // Keeping ordinary OCR text in one request avoids serial model round trips.
    private const int PreferredChunkLength = 6000;
    private const int RetryChunkLength = 2400;
    private const int MinimumChunkLength = 700;
    private const int ContextTailLength = 500;

    private readonly HttpClient _httpClient;

    public OpenAiCompatibleTranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TranslationResult> TranslateAsync(
        string sourceText,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return TranslationResult.FromError("没有可翻译的文本。");
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return TranslationResult.FromError("请先在设置里填写 API Key。");
        }

        var endpoint = BuildEndpoint(settings.BaseUrl);

        try
        {
            var chunks = TranslationChunkingHelper.BuildChunks(sourceText, PreferredChunkLength);
            var sourceContext = new StringBuilder();
            var translatedContext = new StringBuilder();

            var result = await TranslateChunkSequenceAsync(
                chunks,
                endpoint,
                settings,
                sourceContext,
                translatedContext,
                PreferredChunkLength,
                cancellationToken);

            return result.Success
                ? TranslationResult.FromText(result.Text.Trim())
                : result;
        }
        catch (Exception ex)
        {
            return TranslationResult.FromError($"API 翻译执行失败：{ex.Message}");
        }
    }

    private async Task<TranslationResult> TranslateChunkSequenceAsync(
        IReadOnlyList<string> chunks,
        string endpoint,
        AppSettings settings,
        StringBuilder sourceContext,
        StringBuilder translatedContext,
        int chunkLengthHint,
        CancellationToken cancellationToken)
    {
        var translatedChunks = new List<string>(chunks.Count);

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            if (TranslationChunkingHelper.IsLineBreakToken(chunk))
            {
                translatedChunks.Add(chunk);
                AppendContext(sourceContext, chunk);
                AppendContext(translatedContext, chunk);
                continue;
            }

            var chunkResult = await TranslateSingleChunkAsync(
                chunk,
                endpoint,
                settings,
                sourceContext.ToString(),
                translatedContext.ToString(),
                index + 1,
                chunks.Count,
                cancellationToken);

            if (!chunkResult.Success)
            {
                var retryLength = Math.Max(
                    MinimumChunkLength,
                    Math.Min(RetryChunkLength, Math.Max(MinimumChunkLength, chunkLengthHint / 2)));

                if (chunk.Length <= MinimumChunkLength || retryLength >= chunk.Length)
                {
                    return chunkResult;
                }

                var smallerChunks = TranslationChunkingHelper.BuildChunks(chunk, retryLength);
                if (smallerChunks.Count <= 1)
                {
                    return chunkResult;
                }

                var retryResult = await TranslateChunkSequenceAsync(
                    smallerChunks,
                    endpoint,
                    settings,
                    sourceContext,
                    translatedContext,
                    retryLength,
                    cancellationToken);

                if (!retryResult.Success)
                {
                    return TranslationResult.FromError(
                        $"API 翻译在第 {index + 1}/{chunks.Count} 段细分重试后仍失败：{retryResult.ErrorMessage}");
                }

                translatedChunks.Add(retryResult.Text);
                continue;
            }

            translatedChunks.Add(chunkResult.Text);
            AppendContext(sourceContext, chunk);
            AppendContext(translatedContext, chunkResult.Text);
        }

        return TranslationResult.FromText(string.Concat(translatedChunks));
    }

    private async Task<TranslationResult> TranslateSingleChunkAsync(
        string sourceText,
        string endpoint,
        AppSettings settings,
        string previousSourceContext,
        string previousTranslatedContext,
        int chunkIndex,
        int chunkCount,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        var payload = new
        {
            model = settings.Model,
            temperature = settings.Temperature,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt(settings.SystemPrompt, chunkCount > 1, settings.EnableApiContext)
                },
                new
                {
                    role = "user",
                    content = BuildUserPrompt(
                        settings.TargetLanguage,
                        sourceText,
                        settings.EnableApiContext ? previousSourceContext : string.Empty,
                        settings.EnableApiContext ? previousTranslatedContext : string.Empty,
                        chunkIndex,
                        chunkCount)
                }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return TranslationResult.FromError(
                $"翻译请求失败：{(int)response.StatusCode} {response.ReasonPhrase}\n{TrimErrorContent(content)}");
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var translatedElement))
            {
                return TranslationResult.FromError("翻译接口返回格式异常。");
            }

            var translated = translatedElement.GetString();
            return string.IsNullOrWhiteSpace(translated)
                ? TranslationResult.FromError("翻译接口返回了空内容。")
                : TranslationResult.FromText(translated.Trim());
        }
        catch (JsonException ex)
        {
            return TranslationResult.FromError($"翻译接口返回解析失败：{ex.Message}");
        }
    }

    private static string BuildSystemPrompt(string basePrompt, bool useChunkedMode, bool enableContext)
    {
        if (!useChunkedMode || !enableContext)
        {
            return basePrompt;
        }

        return
            $"{basePrompt}\n\n" +
            "You may receive a chunk from a longer text plus previous source/translation context for consistency.\n" +
            "Translate only the current chunk.\n" +
            "Keep terminology, tone, pronouns, and tense consistent with the provided previous translation context.\n" +
            "Do not summarize, explain, or add notes.\n" +
            "Preserve paragraph structure whenever possible.";
    }

    private static string BuildUserPrompt(
        string targetLanguage,
        string currentChunk,
        string previousSourceContext,
        string previousTranslatedContext,
        int chunkIndex,
        int chunkCount)
    {
        var builder = new StringBuilder();
        builder.Append("Target language: ").AppendLine(targetLanguage);

        if (chunkCount > 1)
        {
            builder.Append("Current chunk: ").Append(chunkIndex).Append('/').Append(chunkCount).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(previousSourceContext))
        {
            builder.AppendLine();
            builder.AppendLine("Previous source context (reference only):");
            builder.AppendLine(previousSourceContext);
        }

        if (!string.IsNullOrWhiteSpace(previousTranslatedContext))
        {
            builder.AppendLine();
            builder.AppendLine("Previous translated context (reference only):");
            builder.AppendLine(previousTranslatedContext);
        }

        builder.AppendLine();
        builder.AppendLine("Current text to translate:");
        builder.Append(currentChunk);
        return builder.ToString();
    }

    private static void AppendContext(StringBuilder builder, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        builder.Append(text);
        if (builder.Length <= ContextTailLength)
        {
            return;
        }

        builder.Remove(0, builder.Length - ContextTailLength);
    }

    private static string TrimErrorContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Trim();
        return normalized.Length <= 320
            ? normalized
            : normalized[..320];
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.deepseek.com"
            : baseUrl.Trim().TrimEnd('/');

        return normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}/chat/completions";
    }
}
