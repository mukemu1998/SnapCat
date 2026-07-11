using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IVisualPromptService
{
    Task<VisualPromptResult> AnalyzeAsync(
        string imagePath,
        AiProviderProfile profile,
        CancellationToken cancellationToken = default);
}
