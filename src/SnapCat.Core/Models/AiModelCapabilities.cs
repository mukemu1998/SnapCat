namespace SnapCat.Core.Models;

[Flags]
public enum AiModelCapabilities
{
    None = 0,
    VisionAnalysis = 1 << 0,
    TextToImage = 1 << 1,
    ImageToImage = 1 << 2,
    MultipleReferenceImages = 1 << 3,
    MaskEditing = 1 << 4,
    BackgroundRemoval = 1 << 5,
    TransparentBackground = 1 << 6,
    Resolution2K = 1 << 7,
    Resolution4K = 1 << 8,
    CharacterConsistency = 1 << 9,
    PoseControl = 1 << 10,
    MultiViewGeneration = 1 << 11
}
