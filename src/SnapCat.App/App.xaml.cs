using System.IO;
using System.Net.Http;
using SnapCat.App.Services;
using SnapCat.App.Windows;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using SnapCat.Infrastructure.Services;
using WpfApplication = System.Windows.Application;
using ExitEventArgs = System.Windows.ExitEventArgs;
using StartupEventArgs = System.Windows.StartupEventArgs;
using DispatcherPriority = System.Windows.Threading.DispatcherPriority;

namespace SnapCat.App;

public partial class App : WpfApplication
{
    private const string SingleInstanceMutexName = @"Local\SnapCat-HaG-Cat-SingleInstance";
    private static readonly TimeSpan StartupSettingsLoadTimeout = TimeSpan.FromSeconds(5);
    private readonly string _appDataDirectory;
    private readonly UserDataLocationService _userDataLocationService;
    private Mutex? _singleInstanceMutex;
    private bool _pinnedWindowsPreparedForExit;
    private bool _runtimeServicesInitialized;

    public App()
    {
        _userDataLocationService = new UserDataLocationService();
        _appDataDirectory = _userDataLocationService.ResolveUserDataDirectory();

        SettingsStore = new JsonSettingsStore(_appDataDirectory);
        ThemeService = new ThemeService();
        ThemeService.ApplyTheme(this, null);
    }

    private void InitializeRuntimeServices()
    {
        if (_runtimeServicesInitialized)
        {
            return;
        }

        HistoryStore = new JsonHistoryStore(_appDataDirectory);
        PinnedWindowLayoutStore = new PinnedWindowLayoutStore(_appDataDirectory);
        var tesseractCliOcrService = new TesseractCliOcrService();
        var enhancedTesseractOcrService = new EnhancedTesseractOcrService(tesseractCliOcrService);
        var windowsMediaOcrService = new WindowsMediaOcrService();
        OcrService = new SmartOcrService(
            windowsMediaOcrService,
            enhancedTesseractOcrService,
            tesseractCliOcrService);
        QrCodeService = new ZxingQrCodeService();
        var translationHttpClient = new HttpClient();
        var openAiCompatibleTranslationService = new OpenAiCompatibleTranslationService(translationHttpClient);
        var lightweightWebTranslationService = new LightweightWebTranslationService(translationHttpClient);
        TranslationService = new SmartTranslationService(
            openAiCompatibleTranslationService,
            lightweightWebTranslationService);
        TranslationSpeechService = new TranslationSpeechService();
        ScreenCaptureService = new ScreenCaptureService();
        CapturedImageFileService = new CapturedImageFileService();
        GlobalHotkeyService = new GlobalHotkeyService();
        TrayIconService = new TrayIconService();
        PinnedWindowRegistryService = new PinnedWindowRegistryService(PinnedWindowLayoutStore);
        StartupRegistrationService = new StartupRegistrationService();
        StartupDiagnosticsService = new StartupDiagnosticsService();
        CaptureActionService = new CaptureActionService(
            OcrService,
            TranslationService,
            QrCodeService,
            HistoryStore,
            CapturedImageFileService,
            ScreenCaptureService);

        _runtimeServicesInitialized = true;
    }

    public ISettingsStore SettingsStore { get; }

    public UserDataLocationService UserDataLocationService => _userDataLocationService;

    public string UserDataDirectory => _appDataDirectory;

    public IHistoryStore HistoryStore { get; private set; } = null!;

    public PinnedWindowLayoutStore PinnedWindowLayoutStore { get; private set; } = null!;

    public IOcrService OcrService { get; private set; } = null!;

    public IQrCodeService QrCodeService { get; private set; } = null!;

    public ITranslationService TranslationService { get; private set; } = null!;

    public TranslationSpeechService TranslationSpeechService { get; private set; } = null!;

    public ScreenCaptureService ScreenCaptureService { get; private set; } = null!;

    public CapturedImageFileService CapturedImageFileService { get; private set; } = null!;

    public GlobalHotkeyService GlobalHotkeyService { get; private set; } = null!;

    public TrayIconService TrayIconService { get; private set; } = null!;

    public PinnedWindowRegistryService PinnedWindowRegistryService { get; private set; } = null!;

    public StartupRegistrationService StartupRegistrationService { get; private set; } = null!;

    public StartupDiagnosticsService StartupDiagnosticsService { get; private set; } = null!;

    public ThemeService ThemeService { get; }

    public CaptureActionService CaptureActionService { get; private set; } = null!;

    public AppSettings StartupSettingsSnapshot { get; private set; } = new();

    public string StartupSettingsWarning { get; private set; } = string.Empty;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var startInTray = e.Args.Any(static arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        var startupSplash = ShowStartupSplashIfNeeded(startInTray);

        try
        {
            var startupSettings = await LoadStartupSettingsSafelyAsync();
            StartupSettingsSnapshot = startupSettings;
            ThemeService.ApplyTheme(this, startupSettings.ThemeId);
            InitializeRuntimeServices();

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            CloseStartupSplashWhenMainWindowIsReady(startupSplash);
            _ = Dispatcher.BeginInvoke(
                () => PinnedWindowRegistryService.RestorePersistedWindows(startupSettings),
                DispatcherPriority.ApplicationIdle);
            _ = Dispatcher.BeginInvoke(
                () => _ = CleanupExpiredLocalDataAsync(startupSettings),
                DispatcherPriority.ApplicationIdle);

            if (startInTray)
            {
                mainWindow.StartInTray();
            }
        }
        catch (Exception ex)
        {
            StartupSettingsWarning = $"启动失败：{ex.Message}";
            InitializeRuntimeServices();
            CloseStartupSplashWhenMainWindowIsReady(startupSplash);
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
    }

    private StartupSplashWindow? ShowStartupSplashIfNeeded(bool startInTray)
    {
        if (startInTray)
        {
            return null;
        }

        var splash = new StartupSplashWindow();
        splash.Show();
        splash.UpdateLayout();
        Dispatcher.Invoke(static () => { }, DispatcherPriority.Render);
        return splash;
    }

    private void CloseStartupSplashWhenMainWindowIsReady(StartupSplashWindow? splash)
    {
        if (splash is null)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (splash.IsVisible)
            {
                splash.Close();
            }
        }, DispatcherPriority.ContextIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_runtimeServicesInitialized)
        {
            PreparePinnedWindowsForExit();
            GlobalHotkeyService.Dispose();
            TrayIconService.Dispose();
        }

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    public void PreparePinnedWindowsForExit()
    {
        if (_pinnedWindowsPreparedForExit)
        {
            return;
        }

        _pinnedWindowsPreparedForExit = true;
        PinnedWindowRegistryService.CloseAllWindows(preserveState: true);
    }

    private async Task CleanupExpiredLocalDataAsync(AppSettings settings)
    {
        try
        {
            CapturedImageFileService.CleanupTempFilesOlderThan(settings.TempFileRetentionDays);

            if (settings.HistoryRetentionDays > 0)
            {
                await HistoryStore.DeleteOlderThanAsync(DateTimeOffset.Now.AddDays(-settings.HistoryRetentionDays));
            }
        }
        catch
        {
            // 自动清理失败不应影响启动和截图主流程。
        }
    }

    private async Task<AppSettings> LoadStartupSettingsSafelyAsync()
    {
        try
        {
            var loadTask = SettingsStore.LoadAsync();
            var timeoutTask = Task.Delay(StartupSettingsLoadTimeout);
            var completedTask = await Task.WhenAny(loadTask, timeoutTask);
            if (completedTask != loadTask)
            {
                StartupSettingsWarning = "读取本地设置超时，已用默认配置启动。请检查用户配置目录是否位于很慢或不可访问的位置。";
                return new AppSettings();
            }

            StartupSettingsWarning = string.Empty;
            return await loadTask;
        }
        catch (Exception ex)
        {
            StartupSettingsWarning = $"读取本地设置失败，已用默认配置启动：{ex.Message}";
            return new AppSettings();
        }
    }
}
