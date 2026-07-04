using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class TesseractCliOcrService : IOcrService
{
    public Task<OcrResult> RecognizeAsync(
        string imagePath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        return RecognizeAsync(imagePath, settings, 6, cancellationToken);
    }

    public async Task<OcrResult> RecognizeAsync(
        string imagePath,
        AppSettings settings,
        int pageSegmentationMode,
        CancellationToken cancellationToken = default)
    {
        var executablePath = string.IsNullOrWhiteSpace(settings.TesseractExecutablePath)
            ? "tesseract.exe"
            : settings.TesseractExecutablePath.Trim();

        var debugHeader =
            $"引擎：兼容模式（Tesseract 单轮）{Environment.NewLine}" +
            $"图片：{imagePath}{Environment.NewLine}" +
            $"语言：{settings.TesseractLanguage}{Environment.NewLine}" +
            $"PSM：{pageSegmentationMode}";

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = $"\"{imagePath}\" stdout -l {settings.TesseractLanguage} --psm {pageSegmentationMode}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();

            if (process.ExitCode != 0)
            {
                var message = string.IsNullOrWhiteSpace(error) ? "未知错误。" : error;
                return OcrResult.FromError(
                    $"Tesseract OCR 执行失败：{message}",
                    "tesseract-cli",
                    $"{debugHeader}{Environment.NewLine}结果：失败");
            }

            return string.IsNullOrWhiteSpace(output)
                ? OcrResult.FromError(
                    "OCR 识别结果为空。",
                    "tesseract-cli",
                    $"{debugHeader}{Environment.NewLine}结果：空文本")
                : OcrResult.FromText(
                    output,
                    "tesseract-cli",
                    $"{debugHeader}{Environment.NewLine}结果：成功");
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            return OcrResult.FromError(
                "未找到 tesseract.exe。请先安装 Tesseract OCR，或在设置中填写正确路径。",
                "tesseract-cli",
                $"{debugHeader}{Environment.NewLine}结果：未找到可执行文件");
        }
        catch (Exception ex)
        {
            return OcrResult.FromError(
                $"OCR 识别失败：{ex.Message}",
                "tesseract-cli",
                $"{debugHeader}{Environment.NewLine}结果：异常");
        }
    }
}
