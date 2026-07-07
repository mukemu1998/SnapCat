namespace SnapCat.Core.Models;

public sealed record OcrTextRegion(
    string Text,
    double X,
    double Y,
    double Width,
    double Height);
