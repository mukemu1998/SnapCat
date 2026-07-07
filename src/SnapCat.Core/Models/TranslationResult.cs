namespace SnapCat.Core.Models;

public sealed class TranslationResult
{
    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public static TranslationResult FromText(string text) => new()
    {
        Success = true,
        Text = text
    };

    public static TranslationResult FromError(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
