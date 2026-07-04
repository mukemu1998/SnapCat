using System.Text;
using System.Text.Json;
using System.Net;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class LightweightWebTranslationService : ITranslationService
{
    private const int PreferredChunkLength = 180;
    private const int RetryChunkLength = 110;
    private const int MinimumChunkLength = 60;

    private readonly HttpClient _httpClient;

    public LightweightWebTranslationService(HttpClient httpClient)
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

        var targetLanguage = string.IsNullOrWhiteSpace(settings.TargetLanguage)
            ? "zh-CN"
            : settings.TargetLanguage.Trim();
        var sourceLanguage = DetectSourceLanguage(sourceText, targetLanguage);

        try
        {
            var chunks = TranslationChunkingHelper.BuildChunks(sourceText, PreferredChunkLength);
            var translatedChunks = new List<string>(chunks.Count);

            for (var index = 0; index < chunks.Count; index++)
            {
                var chunk = chunks[index];
                var result = await TranslateChunkWithFallbackAsync(
                    chunk,
                    sourceLanguage,
                    targetLanguage,
                    PreferredChunkLength,
                    cancellationToken);

                if (!result.Success)
                {
                    return TranslationResult.FromError(
                        chunks.Count == 1
                            ? result.ErrorMessage
                            : $"默认轻量翻译在第 {index + 1}/{chunks.Count} 段失败：{result.ErrorMessage}");
                }

                translatedChunks.Add(result.Text);
            }

            var translated = string.Concat(translatedChunks);
            return string.IsNullOrWhiteSpace(translated)
                ? TranslationResult.FromError("默认轻量翻译返回了空内容。")
                : TranslationResult.FromText(translated.Trim());
        }
        catch (Exception ex)
        {
            return TranslationResult.FromError($"默认轻量翻译失败：{ex.Message}");
        }
    }

    private async Task<TranslationResult> TranslateChunkWithFallbackAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        int chunkLengthHint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return TranslationResult.FromText(string.Empty);
        }

        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return TranslationResult.FromText(sourceText);
        }

        var directResult = await TranslateChunkAsync(sourceText, sourceLanguage, targetLanguage, cancellationToken);
        if (directResult.Success || sourceText.Length <= MinimumChunkLength)
        {
            return directResult;
        }

        var retryChunkLength = Math.Max(
            MinimumChunkLength,
            Math.Min(RetryChunkLength, Math.Max(MinimumChunkLength, chunkLengthHint / 2)));

        if (retryChunkLength >= sourceText.Length)
        {
            return directResult;
        }

        var smallerChunks = TranslationChunkingHelper.BuildChunks(sourceText, retryChunkLength);
        if (smallerChunks.Count <= 1)
        {
            return directResult;
        }

        var translatedChunks = new List<string>(smallerChunks.Count);
        for (var index = 0; index < smallerChunks.Count; index++)
        {
            var nestedResult = await TranslateChunkWithFallbackAsync(
                smallerChunks[index],
                sourceLanguage,
                targetLanguage,
                retryChunkLength,
                cancellationToken);

            if (!nestedResult.Success)
            {
                return TranslationResult.FromError(
                    $"默认轻量翻译细分重试失败（第 {index + 1}/{smallerChunks.Count} 小段）：{nestedResult.ErrorMessage}");
            }

            translatedChunks.Add(nestedResult.Text);
        }

        return TranslationResult.FromText(string.Concat(translatedChunks));
    }

    private async Task<TranslationResult> TranslateChunkAsync(
        string sourceText,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var endpoint =
            "https://api.mymemory.translated.net/get" +
            $"?q={Uri.EscapeDataString(sourceText)}&langpair={Uri.EscapeDataString(sourceLanguage)}|{Uri.EscapeDataString(targetLanguage)}";

        using var response = await _httpClient.GetAsync(endpoint, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return TranslationResult.FromError(
                $"默认轻量翻译请求失败：{(int)response.StatusCode} {response.ReasonPhrase}");
        }

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        if (!root.TryGetProperty("responseStatus", out var statusElement)
            || !TryReadInt(statusElement, out var responseStatus)
            || responseStatus != 200)
        {
            var details = root.TryGetProperty("responseDetails", out var detailsElement)
                ? detailsElement.GetString()
                : null;

            return TranslationResult.FromError($"默认轻量翻译返回异常：{details ?? "未知错误。"}");
        }

        if (!root.TryGetProperty("responseData", out var responseData)
            || !responseData.TryGetProperty("translatedText", out var translatedElement))
        {
            return TranslationResult.FromError("默认轻量翻译返回格式异常。");
        }

        var translated = NormalizeTranslatedText(translatedElement.GetString());
        return string.IsNullOrWhiteSpace(translated)
            ? TranslationResult.FromError("默认轻量翻译返回了空内容。")
            : TranslationResult.FromText(translated);
    }

    private static string NormalizeTranslatedText(string? translated)
    {
        if (string.IsNullOrWhiteSpace(translated))
        {
            return string.Empty;
        }

        var normalized = WebUtility.HtmlDecode(translated)
            .Replace("\u00A0", " ")
            .Replace("\u200B", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var lines = normalized
            .Split('\n')
            .Select(static line => line.TrimEnd());

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static bool TryReadInt(JsonElement element, out int value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetInt32(out value);
            case JsonValueKind.String:
                return int.TryParse(element.GetString(), out value);
            default:
                value = default;
                return false;
        }
    }

    private static string DetectSourceLanguage(string sourceText, string targetLanguage)
    {
        foreach (var character in sourceText)
        {
            if (character is >= '\u3040' and <= '\u30ff')
            {
                return "ja";
            }

            if (character is >= '\uac00' and <= '\ud7af')
            {
                return "ko";
            }

            if (character is >= '\u0400' and <= '\u04ff')
            {
                return "ru";
            }
        }

        if (sourceText.Any(static character => character is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff'))
        {
            return "zh-CN";
        }

        if (sourceText.Any(char.IsLetter))
        {
            return "en";
        }

        return targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "zh-CN";
    }
}
