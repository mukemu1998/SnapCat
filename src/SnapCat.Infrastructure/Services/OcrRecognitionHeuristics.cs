using SnapCat.Core.Models;

namespace SnapCat.Infrastructure.Services;

internal static class OcrRecognitionHeuristics
{
    public static string NormalizeText(string text)
    {
        var lines = text
            .ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.Trim());

        return string.Join(Environment.NewLine, lines);
    }

    public static string CreatePreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        var singleLine = text.ReplaceLineEndings(" ").Trim();
        const int maxLength = 80;
        return singleLine.Length <= maxLength
            ? singleLine
            : $"{singleLine[..maxLength]}...";
    }

    public static double ScoreText(string text, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return double.MinValue;
        }

        var normalized = NormalizeText(text);
        if (normalized.Length < 2)
        {
            return -1000;
        }

        var totalLength = normalized.Length;
        var lettersOrDigits = 0;
        var spaces = 0;
        var cjk = 0;
        var punctuation = 0;
        var suspicious = 0;
        var repeatPenalty = 0;
        var lineBreakBonus = 0;
        var previous = '\0';
        var repeatedCount = 0;

        foreach (var character in normalized)
        {
            if (character == previous)
            {
                repeatedCount++;
                if (repeatedCount >= 3)
                {
                    repeatPenalty++;
                }
            }
            else
            {
                repeatedCount = 0;
                previous = character;
            }

            if (char.IsLetterOrDigit(character))
            {
                lettersOrDigits++;
                continue;
            }

            if (IsCjk(character))
            {
                cjk++;
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                spaces++;
                if (character is '\r' or '\n')
                {
                    lineBreakBonus++;
                }

                continue;
            }

            if (IsCommonPunctuation(character))
            {
                punctuation++;
                continue;
            }

            suspicious++;
        }

        var expectsCjk = settings.TesseractLanguage.Contains("chi", StringComparison.OrdinalIgnoreCase)
            || settings.TesseractLanguage.Contains("jpn", StringComparison.OrdinalIgnoreCase)
            || settings.TesseractLanguage.Contains("kor", StringComparison.OrdinalIgnoreCase);

        var cjkWeight = expectsCjk ? 2.5d : 0.8d;
        var latinWeight = expectsCjk ? 1.3d : 1.7d;

        return (totalLength * 1.15d)
            + (lettersOrDigits * latinWeight)
            + (cjk * cjkWeight)
            + (spaces * 0.15d)
            + (lineBreakBonus * 0.6d)
            + (punctuation * 0.75d)
            - (suspicious * 4.5d)
            - (repeatPenalty * 3.0d);
    }

    private static bool IsCjk(char character)
    {
        return character is >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff';
    }

    private static bool IsCommonPunctuation(char character)
    {
        return character is '.' or ',' or '!' or '?' or ':' or ';' or '\'' or '"' or '-'
            or '(' or ')' or '[' or ']' or '{' or '}' or '/' or '\\' or '#'
            or '&' or '%' or '+' or '=' or '_' or '*'
            or '，' or '。' or '！' or '？' or '：' or '；' or '（' or '）'
            or '【' or '】' or '「' or '」' or '、' or '《' or '》' or '“' or '”';
    }
}
