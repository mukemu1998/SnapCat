namespace SnapCat.Core.Models;

public sealed class QrCodeResult
{
    public bool Success { get; init; }

    public string Text { get; init; } = string.Empty;

    public string ErrorMessage { get; init; } = string.Empty;

    public static QrCodeResult FromText(string text) => new()
    {
        Success = true,
        Text = text
    };

    public static QrCodeResult FromError(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
