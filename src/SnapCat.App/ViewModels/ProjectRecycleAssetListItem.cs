using System.IO;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;

namespace SnapCat.App.ViewModels;

internal sealed class ProjectRecycleAssetListItem
{
    public ProjectRecycleAssetListItem(ProjectAsset asset, string recycleDirectory)
    {
        Asset = asset;
        Thumbnail = ImageThumbnailLoader.Load(ResolveThumbnailPath(asset, recycleDirectory), 320);
    }

    public ProjectAsset Asset { get; }

    public ImageSource? Thumbnail { get; }

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

    private static string? ResolveThumbnailPath(ProjectAsset asset, string recycleDirectory)
    {
        if (!Directory.Exists(recycleDirectory))
        {
            return null;
        }

        foreach (var relativePath in new[] { asset.ThumbnailRelativePath, asset.RelativePath })
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var fileName = Path.GetFileName(relativePath);
            var candidate = Directory.EnumerateFiles(recycleDirectory, $"{asset.Id}-*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => path.EndsWith($"-{fileName}", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
