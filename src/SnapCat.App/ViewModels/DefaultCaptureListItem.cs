using System.IO;
using System.Windows.Media;
using SnapCat.App.Services;

namespace SnapCat.App;

internal sealed record DefaultCaptureListItem(string Path)
{
    public ImageSource? Thumbnail { get; } = ImageThumbnailLoader.Load(Path);

    public string Title => System.IO.Path.GetFileName(Path);

    public string Summary
    {
        get
        {
            try
            {
                var info = new FileInfo(Path);
                return $"{info.LastWriteTime:yyyy-MM-dd HH:mm:ss} | {FormatFileSize(info.Length)} | {Path}";
            }
            catch
            {
                return Path;
            }
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.##} MB";
        }

        return $"{Math.Max(1, bytes / 1024d):0.#} KB";
    }
}
