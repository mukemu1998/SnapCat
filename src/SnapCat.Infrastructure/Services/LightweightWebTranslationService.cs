using System.Text;
using System.Text.Json;
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
            var chunks = BuildChunks(sourceText, PreferredChunkLength);
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

        var smallerChunks = BuildChunks(sourceText, retryChunkLength);
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

        var translated = translatedElement.GetString();
        return string.IsNullOrWhiteSpace(translated)
            ? TranslationResult.FromError("默认轻量翻译返回了空内容。")
            : TranslationResult.FromText(translated);
    }

    private static List<string> BuildChunks(string sourceText, int maxChunkLength)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var block in SplitByLineBreaks(sourceText))
        {
            if (block.Length == 0)
            {
                continue;
            }

            if (IsLineBreakToken(block))
            {
                FlushCurrent(chunks, current);
                chunks.Add(block);
                continue;
            }

            foreach (var unit in SplitIntoSentenceUnits(block))
            {
                foreach (var piece in SplitLongUnit(unit, maxChunkLength))
                {
                    if (current.Length + piece.Length > maxChunkLength && current.Length > 0)
                    {
                        FlushCurrent(chunks, current);
                    }

                    current.Append(piece);
                }
            }
        }

        FlushCurrent(chunks, current);
        return chunks;
    }

    private static IEnumerable<string> SplitByLineBreaks(string sourceText)
    {
        var start = 0;

        for (var index = 0; index < sourceText.Length; index++)
        {
            if (sourceText[index] != '\r' && sourceText[index] != '\n')
            {
                continue;
            }

            if (index > start)
            {
                yield return sourceText[start..index];
            }

            var lineBreakStart = index;
            while (index < sourceText.Length && (sourceText[index] == '\r' || sourceText[index] == '\n'))
            {
                index++;
            }

            yield return sourceText[lineBreakStart..index];
            start = index;
            index--;
        }

        if (start < sourceText.Length)
        {
            yield return sourceText[start..];
        }
    }

    private static IEnumerable<string> SplitIntoSentenceUnits(string text)
    {
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (!IsSentenceBoundary(text, index))
            {
                continue;
            }

            var end = index + 1;
            while (end < text.Length && IsTrailingSentenceCharacter(text[end]))
            {
                end++;
            }

            yield return text[start..end];
            start = end;
            index = end - 1;
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    private static IEnumerable<string> SplitLongUnit(string unit, int maxChunkLength)
    {
        if (unit.Length <= maxChunkLength)
        {
            yield return unit;
            yield break;
        }

        var startIndex = 0;
        while (startIndex < unit.Length)
        {
            var remainingLength = unit.Length - startIndex;
            var takeLength = Math.Min(maxChunkLength, remainingLength);
            if (takeLength < remainingLength)
            {
                var splitIndex = FindSplitIndex(unit.AsSpan(startIndex, takeLength));
                takeLength = splitIndex > 0 ? splitIndex : takeLength;
            }

            yield return unit.Substring(startIndex, takeLength);
            startIndex += takeLength;
        }
    }

    private static bool IsSentenceBoundary(string text, int index)
    {
        var character = text[index];
        return character switch
        {
            '。' or '！' or '？' or '；' or '…' => true,
            '.' => !IsDigitAround(text, index),
            '!' or '?' or ';' => true,
            _ => false
        };
    }

    private static bool IsTrailingSentenceCharacter(char character)
        => char.IsWhiteSpace(character)
           || character is '"' or '\'' or ')' or ']' or '}' or '”' or '’' or '》' or '】' or '）';

    private static bool IsDigitAround(string text, int index)
    {
        var hasDigitBefore = index > 0 && char.IsDigit(text[index - 1]);
        var hasDigitAfter = index + 1 < text.Length && char.IsDigit(text[index + 1]);
        return hasDigitBefore || hasDigitAfter;
    }

    private static int FindSplitIndex(ReadOnlySpan<char> span)
    {
        for (var index = span.Length - 1; index >= 0; index--)
        {
            if (char.IsWhiteSpace(span[index])
                || span[index] is '，' or '、' or '：' or '；' or '。' or ',' or ':' or ';')
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static bool IsLineBreakToken(string token)
        => token.All(static character => character is '\r' or '\n');

    private static void FlushCurrent(List<string> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        chunks.Add(current.ToString());
        current.Clear();
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
