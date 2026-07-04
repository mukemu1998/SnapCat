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
    {
        try
        {
            using var bitmap = (Bitmap)Image.FromFile(imagePath);

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
            if (result is null || string.IsNullOrWhiteSpace(result.Text))
            {
                return Task.FromResult(QrCodeResult.FromError("未识别到二维码内容。"));
            }

            return Task.FromResult(QrCodeResult.FromText(result.Text));
        }
        catch (Exception ex)
        {
            return Task.FromResult(QrCodeResult.FromError($"二维码识别失败：{ex.Message}"));
        }
    }
}
