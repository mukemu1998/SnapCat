using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(string sourceText, AppSettings settings, CancellationToken cancellationToken = default);
}
