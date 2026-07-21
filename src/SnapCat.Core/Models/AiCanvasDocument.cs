namespace SnapCat.Core.Models;

/// <summary>
/// Project-local state for the regular AI canvas. Nodes reference stable asset IDs only.
/// </summary>
public sealed class AiCanvasDocument
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "AI 创作画布";

    public double ViewportOffsetX { get; set; }

    public double ViewportOffsetY { get; set; }

    public double ViewportScale { get; set; } = 1d;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    // Ordered references stay separate from positioned canvas nodes.
    public List<string> ReferenceAssetIds { get; set; } = [];

    // The project owns creative intent, while provider credentials remain user-local.
    public AiCanvasGenerationDraft GenerationDraft { get; set; } = new();

    public List<AiCanvasAssetNode> AssetNodes { get; set; } = [];
}

public sealed class AiCanvasGenerationDraft
{
    public string Prompt { get; set; } = string.Empty;

    public string NegativePrompt { get; set; } = string.Empty;

    public string AspectRatio { get; set; } = "1:1";

    public string ReferenceIntent { get; set; } = "综合参考";

    // Cloud-facing generations remain single-result unless the user explicitly changes this value.
    public int OutputCount { get; set; } = 1;
}

public sealed class AiCanvasAssetNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string AssetId { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; } = 240d;

    public double Height { get; set; } = 180d;

    // Standard cards stay compact by default; users can opt into native image dimensions per node.
    public bool UseOriginalSize { get; set; }

    public int ZIndex { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
