using System.IO;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed record ProjectAssetListItem(
    ProjectAsset Asset,
    string SourcePath,
    string ThumbnailPath,
    int ReferenceOrder = 0)
{
    public ImageSource? Thumbnail { get; } = ImageThumbnailLoader.Load(
        File.Exists(ThumbnailPath) ? ThumbnailPath : SourcePath,
        160);

    public string Title => Asset.DisplayName;

    public string ReferenceOrderLabel => ReferenceOrder > 0 ? ReferenceOrder.ToString() : string.Empty;

    public string Summary => $"{CategoryLabel} | {GetKindLabel(Asset.Kind)} | v{Asset.Version}{GetVersionSourceLabel()} | {Asset.CreatedAt:yyyy-MM-dd HH:mm}";

    public string CategoryLabel => string.IsNullOrWhiteSpace(Asset.CustomCategory)
        ? GetCategoryLabel(Asset.Category)
        : Asset.CustomCategory;

    private string GetVersionSourceLabel() => string.IsNullOrWhiteSpace(Asset.ParentAssetId) ? string.Empty : " | 派生版本";

    private static string GetCategoryLabel(ProjectAssetCategory category) => category switch
    {
        ProjectAssetCategory.Character => "角色",
        ProjectAssetCategory.Scene => "场景",
        ProjectAssetCategory.Prop => "道具",
        ProjectAssetCategory.StyleReference => "风格参考",
        _ => "未分类"
    };

    private static string GetKindLabel(ProjectAssetKind kind) => kind switch
    {
        ProjectAssetKind.Generated => "生成结果",
        ProjectAssetKind.Reference => "参考图",
        ProjectAssetKind.Screenshot => "截图",
        _ => "导入素材"
    };
}
