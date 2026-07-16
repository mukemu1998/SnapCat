using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed record ProjectRecycleAssetListItem(ProjectAsset Asset)
{
    public string Title => Asset.DisplayName;

    public string Summary => $"{GetCategoryLabel(Asset.Category)} | v{Asset.Version} | 已移入项目回收站";

    private static string GetCategoryLabel(ProjectAssetCategory category) => category switch
    {
        ProjectAssetCategory.Character => "角色",
        ProjectAssetCategory.Scene => "场景",
        ProjectAssetCategory.Prop => "道具",
        ProjectAssetCategory.StyleReference => "风格参考",
        _ => "未分类"
    };
}
