using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontFamily = System.Drawing.FontFamily;
using DrawingFontStyle = System.Drawing.FontStyle;

namespace SnapCat.App;

public partial class MainWindow
{
    private async void TestOcrButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = BuildCurrentSettings();
        SetTestButtonsEnabled(false);
        OcrTestResultTextBox.Text = "正在测试 OCR，请稍候...";

        var tempFile = string.Empty;

        try
        {
            tempFile = CreateOcrTestImage();
            var result = await _app.OcrService.RecognizeAsync(tempFile, settings);

            OcrTestResultTextBox.Text = result.Success
                ? $"OCR 测试成功。\n\n识别结果：\n{result.Text}\n\n调试信息：\n{result.DebugSummary}"
                : $"OCR 测试失败。\n\n错误信息：\n{result.ErrorMessage}\n\n调试信息：\n{result.DebugSummary}";
        }
        catch (Exception ex)
        {
            OcrTestResultTextBox.Text = $"OCR 测试执行失败：{ex.Message}";
        }
        finally
        {
            TryDeleteFile(tempFile);
            SetTestButtonsEnabled(true);
        }
    }

    private static string CreateOcrTestImage()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapCat", "settings-tests");
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"ocr-test-{DateTime.Now:yyyyMMdd-HHmmssfff}.png");

        using var bitmap = new Bitmap(960, 260);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(DrawingColor.White);

        using var titleFont = CreateFont(30, DrawingFontStyle.Bold);
        using var bodyFont = CreateFont(22, DrawingFontStyle.Regular);

        graphics.DrawString("SnapCat OCR 识别测试 123", titleFont, DrawingBrushes.Black, 28, 34);
        graphics.DrawString("本地识别与接口翻译", bodyFont, DrawingBrushes.Black, 32, 104);
        graphics.DrawString("自由框选 screenshot translation", bodyFont, DrawingBrushes.Black, 32, 154);

        bitmap.Save(filePath, ImageFormat.Png);
        return filePath;
    }

    private static DrawingFont CreateFont(float size, DrawingFontStyle style)
    {
        try
        {
            return new DrawingFont("Microsoft YaHei UI", size, style);
        }
        catch
        {
            return new DrawingFont(DrawingFontFamily.GenericSansSerif, size, style);
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temporary cleanup failures.
        }
    }
}
