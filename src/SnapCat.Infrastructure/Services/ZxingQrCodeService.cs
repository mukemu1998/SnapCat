using System.Drawing;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace SnapCat.Infrastructure.Services;

public sealed class ZxingQrCodeService : IQrCodeService
{
    public Task<QrCodeResult> DecodeAsync(string imagePath, CancellationToken cancellationToken = default)
        => Task.Run(() => Decode(imagePath, cancellationToken), cancellationToken);

    private static QrCodeResult Decode(string imagePath, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var bitmap = (Bitmap)Image.FromFile(imagePath);
            cancellationToken.ThrowIfCancellationRequested();

            var source = new BitmapLuminanceSource(bitmap);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = [BarcodeFormat.QR_CODE]
                }
            };

            var result = reader.Decode(source);
            cancellationToken.ThrowIfCancellationRequested();
            if (result is null || string.IsNullOrWhiteSpace(result.Text))
            {
                return QrCodeResult.FromError("未识别到二维码内容。");
            }

            return QrCodeResult.FromText(result.Text);
        }
        catch (OperationCanceledException)
        {
            return QrCodeResult.FromError("二维码识别已取消。");
        }
        catch (Exception ex)
        {
            return QrCodeResult.FromError($"二维码识别失败：{ex.Message}");
        }
    }
}
