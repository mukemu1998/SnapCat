namespace SnapCat.Core.Models;

public static class ImageGenerationProtocol
{
    public const string ComfyUi = "comfyui";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), ComfyUi, StringComparison.OrdinalIgnoreCase)
            ? ComfyUi
            : ComfyUi;
}

