using SnapCat.Core.Models;

namespace SnapCat.Core.Services;

public interface IQrCodeService
{
    Task<QrCodeResult> DecodeAsync(string imagePath, CancellationToken cancellationToken = default);
}
