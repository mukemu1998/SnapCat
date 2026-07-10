namespace SnapCat.Core.Models;

public static class AiProviderProtocol
{
    public const string OpenAiCompatible = "openai-compatible";

    public const string Gemini = "gemini";

    public const string Ollama = "ollama";

    public const string Custom = "custom";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Gemini => Gemini,
            Ollama => Ollama,
            Custom => Custom,
            _ => OpenAiCompatible
        };
    }
}
