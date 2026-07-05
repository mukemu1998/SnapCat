using System.Diagnostics;
using SnapCat.App.Services;

namespace SnapCat.App;

public partial class MainWindow
{
    private void OpenDirectory(string directory, string label)
    {
        try
        {
            WindowsExplorerService.OpenDirectory(directory, createIfMissing: true);
            StatusTextBlock.Text = $"已打开{label}。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"打开{label}失败：{ex.Message}";
        }
    }

    private void OpenUrl(string url, string label)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            StatusTextBlock.Text = $"已打开{label}。";
            AppendOperationLog($"已打开{label}。");
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"打开{label}失败：{ex.Message}";
            AppendOperationLog($"打开{label}失败：{ex.Message}");
        }
    }

    private void AppendOperationLog(string message)
    {
        _operationLogs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        if (_operationLogs.Count > 8)
        {
            _operationLogs.RemoveRange(8, _operationLogs.Count - 8);
        }

        OperationLogTextBlock.Text = _operationLogs.Count == 0
            ? "最近操作日志：暂无。"
            : "最近操作日志：" + Environment.NewLine + string.Join(Environment.NewLine, _operationLogs.Take(3));
    }
}
