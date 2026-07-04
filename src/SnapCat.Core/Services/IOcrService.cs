using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IOcrService
{
    Task<OcrResult> RecognizeAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default);
}
