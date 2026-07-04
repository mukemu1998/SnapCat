using System.Text;

namespace SnapCat.Infrastructure.Services;

internal static class TranslationChunkingHelper
{
    public static List<string> BuildChunks(string sourceText, int maxChunkLength)
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

    public static bool IsLineBreakToken(string token)
        => token.All(static character => character is '\r' or '\n');

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

    private static void FlushCurrent(List<string> chunks, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        chunks.Add(current.ToString());
        current.Clear();
    }
}
