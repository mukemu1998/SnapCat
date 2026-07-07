namespace SnapCat.Core.Models;

public sealed class OcrResult
{
    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public string EngineName { get; init; } = string.Empty;

    public string DebugSummary { get; init; } = string.Empty;

    public IReadOnlyList<OcrTextRegion> Regions { get; init; } = Array.Empty<OcrTextRegion>();

    public static OcrResult FromText(
        string text,
        string engineName = "",
        string debugSummary = "",
        IReadOnlyList<OcrTextRegion>? regions = null) => new()
    {
        Success = true,
        Text = text,
        EngineName = engineName,
        DebugSummary = debugSummary,
        Regions = regions ?? Array.Empty<OcrTextRegion>()
    };

    public static OcrResult FromError(
        string errorMessage,
        string engineName = "",
        string debugSummary = "") => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        EngineName = engineName,
        DebugSummary = debugSummary
    };
}
