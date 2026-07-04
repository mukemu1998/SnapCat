using System.Windows;
using System.Windows.Threading;

namespace SnapCat.App.Windows;

public partial class StartupSplashWindow : Window
{
    private readonly DispatcherTimer _loadingTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private int _loadingDotCount;

    public StartupSplashWindow()
    {
        InitializeComponent();
        _loadingTimer.Tick += LoadingTimer_OnTick;
        Loaded += (_, _) => _loadingTimer.Start();
        Closed += (_, _) => _loadingTimer.Stop();
    }

    private void LoadingTimer_OnTick(object? sender, EventArgs e)
    {
        _loadingDotCount = (_loadingDotCount + 1) % 4;
        LoadingTextBlock.Text = "加载中" + new string('.', _loadingDotCount);
    }
}
