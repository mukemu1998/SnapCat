using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IImageGenerationService
{
    Task<ImageGenerationConnectionResult> TestConnectionAsync(
        ImageGenerationProfile profile,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetCheckpointModelsAsync(
        ImageGenerationProfile profile,
        CancellationToken cancellationToken = default);

    Task<ImageGenerationResult> GenerateAsync(
        ImageGenerationRequest request,
        ImageGenerationProfile profile,
        CancellationToken cancellationToken = default);
}

