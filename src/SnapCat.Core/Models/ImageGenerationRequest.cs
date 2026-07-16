namespace SnapCat.Core.Models;

public sealed class ImageGenerationRequest
{
    public string Prompt { get; init; } = string.Empty;

    public string NegativePrompt { get; init; } = string.Empty;

    public string Checkpoint { get; init; } = string.Empty;

    public int Width { get; init; } = 1024;

    public int Height { get; init; } = 1024;

    public int Steps { get; init; } = 24;

    public double CfgScale { get; init; } = 7d;

    public int Seed { get; init; } = -1;

    // Keep the first external generation loop intentionally single-result by default.
    public int OutputCount { get; init; } = 1;
}

public sealed class ImageGenerationConnectionResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;
}

public sealed class ImageGenerationOutput
{
    public string FileName { get; init; } = string.Empty;

    public string Subfolder { get; init; } = string.Empty;

    public string Type { get; init; } = "output";

    public byte[] Content { get; init; } = [];
}

public sealed class ImageGenerationResult
{
    public bool Success { get; init; }

    public string TaskId { get; init; } = string.Empty;

    public string ProviderProfileId { get; init; } = string.Empty;

    public string RemoteTaskId { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public IReadOnlyList<ImageGenerationOutput> Outputs { get; init; } = [];

    public static ImageGenerationResult FromError(string message, string taskId = "", string providerProfileId = "") => new()
    {
        ErrorMessage = message,
        TaskId = taskId,
        ProviderProfileId = providerProfileId
    };
}

