namespace SnapCat.Core.Models;

public sealed class AiTaskRequest
{
    public AiTaskKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string ProviderProfileId { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ReferenceImageCount { get; init; }

    public int OutputCount { get; init; } = 1;
}

public sealed class AiTaskRun
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public AiTaskKind Kind { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string ProviderProfileId { get; init; } = string.Empty;

    public string ModelName { get; init; } = string.Empty;

    public int ReferenceImageCount { get; init; }

    public int OutputCount { get; init; } = 1;

    public AiTaskStatus Status { get; internal set; } = AiTaskStatus.Draft;

    public string ErrorMessage { get; internal set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; internal set; } = DateTimeOffset.Now;

    public bool IsTerminal => Status is AiTaskStatus.Cancelled
        or AiTaskStatus.Succeeded
        or AiTaskStatus.Failed
        or AiTaskStatus.Interrupted;

    public AiTaskRun Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        DisplayName = DisplayName,
        ProviderProfileId = ProviderProfileId,
        ModelName = ModelName,
        ReferenceImageCount = ReferenceImageCount,
        OutputCount = OutputCount,
        Status = Status,
        ErrorMessage = ErrorMessage,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt
    };
}
