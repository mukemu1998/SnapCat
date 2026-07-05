using System.IO;
using System.Windows.Media;
using SnapCat.App.Services;
using SnapCat.Core.Models;

namespace SnapCat.App;

internal sealed record PinnedImageListItem(PinnedWindowSnapshot Snapshot)
{
    public string Id => Snapshot.Id;

    public ImageSource? Thumbnail { get; } = ImageThumbnailLoader.Load(Snapshot.ImagePath);

    public string Title
    {
        get
        {
            var fileName = string.IsNullOrWhiteSpace(Snapshot.ImagePath)
                ? "未知图片"
                : Path.GetFileName(Snapshot.ImagePath);
            var groupName = string.IsNullOrWhiteSpace(Snapshot.GroupName) ? "未成组" : Snapshot.GroupName;
            return $"{fileName} · {groupName}";
        }
    }

    public string Summary
    {
        get
        {
            var groupName = string.IsNullOrWhiteSpace(Snapshot.GroupName) ? "未成组" : Snapshot.GroupName;
            var visibility = Snapshot.IsVisible ? "显示中" : "已隐藏";
            var fileName = string.IsNullOrWhiteSpace(Snapshot.ImagePath)
                ? "未知图片"
                : Path.GetFileName(Snapshot.ImagePath);
            return $"{groupName} | {visibility} | {Math.Round(Snapshot.Width)}x{Math.Round(Snapshot.Height)} | X:{Math.Round(Snapshot.Left)} Y:{Math.Round(Snapshot.Top)} | {fileName}";
        }
    }
}
