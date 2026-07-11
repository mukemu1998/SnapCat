using System.Text.Json;
using SnapCat.Core.Models;

namespace SnapCat.Infrastructure.Services;

internal static class VisualPromptResponseParser
{
    public static VisualPromptAnalysis Parse(string response)
    {
        var normalized = ExtractJsonObject(response);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new VisualPromptAnalysis
            {
                PromptZh = response.Trim(),
                PromptEn = response.Trim()
            };
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            var root = document.RootElement;
            return new VisualPromptAnalysis
            {
                Subject = ReadString(root, "subject", "主体"),
                Appearance = ReadString(root, "appearance", "外观", "appearanceAndMaterials"),
                Composition = ReadString(root, "composition", "构图"),
                Lighting = ReadString(root, "lighting", "光影"),
                Style = ReadString(root, "style", "风格"),
                AnalysisEn = ReadString(root, "analysisEn", "analysis_en", "fullEnglishAnalysis", "完整英文分析"),
                SubjectEn = ReadString(root, "subjectEn", "subject_en"),
                AppearanceEn = ReadString(root, "appearanceEn", "appearance_en"),
                CompositionEn = ReadString(root, "compositionEn", "composition_en"),
                LightingEn = ReadString(root, "lightingEn", "lighting_en"),
                StyleEn = ReadString(root, "styleEn", "style_en"),
                PromptZh = ReadString(root, "promptZh", "prompt_zh", "中文提示词"),
                PromptEn = ReadString(root, "promptEn", "prompt_en", "englishPrompt", "English prompt"),
                NegativePrompt = ReadString(root, "negativePrompt", "negative_prompt", "负面提示词"),
                NegativePromptEn = ReadString(root, "negativePromptEn", "negative_prompt_en")
            };
        }
        catch (JsonException)
        {
            return new VisualPromptAnalysis
            {
                PromptZh = response.Trim(),
                PromptEn = response.Trim()
            };
        }
    }

    private static string ReadString(JsonElement root, params string[] names)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()?.Trim() ?? string.Empty
                    : property.Value.ToString().Trim();
            }
        }

        return string.Empty;
    }

    private static string ExtractJsonObject(string? response)
    {
        var trimmed = response?.Trim() ?? string.Empty;
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            trimmed = firstLineEnd >= 0 ? trimmed[(firstLineEnd + 1)..] : string.Empty;
            var fenceEnd = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                trimmed = trimmed[..fenceEnd];
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : string.Empty;
    }
}
