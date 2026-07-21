using System.Windows;
using System.Windows.Input;
using SnapCat.Infrastructure.Services;

namespace SnapCat.App.Windows;

public partial class UpdateProgressWindow : Window
{
    private bool _canClose;

    public UpdateProgressWindow()
    {
        InitializeComponent();
    }

    public void Report(ReleaseUpdateProgressStage stage, string version, double? percent, bool isIndeterminate)
    {
        VersionTextBlock.Text = $"目标版本：{version}";
        UpdateProgressBar.IsIndeterminate = isIndeterminate;
        UpdateProgressBar.Value = percent ?? 0;
        PercentTextBlock.Text = percent is { } progressPercent ? $"{progressPercent:F0}%" : string.Empty;
        StatusTextBlock.Text = stage switch
        {
            ReleaseUpdateProgressStage.Downloading => percent is { } downloadPercent
                ? $"正在下载更新文件：{downloadPercent:F0}%"
                : "正在连接下载服务...",
            ReleaseUpdateProgressStage.Verifying => "下载完成，正在校验文件完整性...",
            ReleaseUpdateProgressStage.Extracting => "校验通过，正在安全解压更新文件...",
            ReleaseUpdateProgressStage.Ready => "更新文件已准备完成，正在启动升级...",
            _ => "正在准备更新..."
        };
    }

    public void ShowFailure(string message)
    {
        _canClose = true;
        CloseButton.IsEnabled = true;
        UpdateProgressBar.IsIndeterminate = false;
        StatusTextBlock.Text = message;
        PercentTextBlock.Text = "失败";
    }

    public void Dismiss()
    {
        _canClose = true;
        Close();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_canClose)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
