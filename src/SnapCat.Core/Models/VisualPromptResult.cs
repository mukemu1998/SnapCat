namespace SnapCat.Core.Models;

public sealed class VisualPromptResult
{
    public bool Success { get; init; }

    public string ErrorMessage { get; init; } = string.Empty;

    public string TaskId { get; init; } = string.Empty;

    public string ProviderProfileId { get; init; } = string.Empty;

    public string RawResponse { get; init; } = string.Empty;

    public VisualPromptAnalysis Analysis { get; init; } = new();

    public static VisualPromptResult FromError(string message, string taskId = "", string providerProfileId = "") => new()
    {
        Success = false,
        ErrorMessage = message,
        TaskId = taskId,
        ProviderProfileId = providerProfileId
    };
}
