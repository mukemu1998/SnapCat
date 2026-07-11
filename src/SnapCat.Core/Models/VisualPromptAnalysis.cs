namespace SnapCat.Core.Models;

public sealed class VisualPromptAnalysis
{
    public string Subject { get; set; } = string.Empty;

    public string Appearance { get; set; } = string.Empty;

    public string Composition { get; set; } = string.Empty;

    public string Lighting { get; set; } = string.Empty;

    public string Style { get; set; } = string.Empty;

    public string AnalysisEn { get; set; } = string.Empty;

    public string SubjectEn { get; set; } = string.Empty;

    public string AppearanceEn { get; set; } = string.Empty;

    public string CompositionEn { get; set; } = string.Empty;

    public string LightingEn { get; set; } = string.Empty;

    public string StyleEn { get; set; } = string.Empty;

    public string PromptZh { get; set; } = string.Empty;

    public string PromptEn { get; set; } = string.Empty;

    public string NegativePrompt { get; set; } = string.Empty;

    public string NegativePromptEn { get; set; } = string.Empty;

    public string ToEditableText()
    {
        var chineseSections = new[]
        {
            ("主体", Subject),
            ("外观与材质", Appearance),
            ("构图与视角", Composition),
            ("光影与色彩", Lighting),
            ("风格", Style),
            ("中文提示词", PromptZh),
            ("负面提示词", NegativePrompt)
        };

        var englishSections = new[]
        {
            ("主体", SubjectEn),
            ("外观与材质", AppearanceEn),
            ("构图与视角", CompositionEn),
            ("光影与色彩", LightingEn),
            ("风格", StyleEn),
            ("完整分析", AnalysisEn),
            ("生图提示词", PromptEn),
            ("负面提示词", NegativePromptEn)
        };

        return string.Join(Environment.NewLine + Environment.NewLine, new[]
        {
            BuildLanguageSection("中文分析", chineseSections),
            BuildLanguageSection("英文分析", englishSections)
        }.Where(static section => !string.IsNullOrWhiteSpace(section)));
    }

    private static string BuildLanguageSection(string title, IEnumerable<(string Title, string Content)> sections)
    {
        var content = string.Join(
            Environment.NewLine + Environment.NewLine,
            sections
                .Where(section => !string.IsNullOrWhiteSpace(section.Content))
                .Select(section => $"{section.Title}\n{section.Content.Trim()}"));
        return string.IsNullOrWhiteSpace(content) ? string.Empty : $"{title}\n{content}";
    }
}
