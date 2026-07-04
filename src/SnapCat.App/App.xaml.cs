using System.IO;
using System.Net.Http;
using SnapCat.App.Services;
using SnapCat.Core.Models;
using SnapCat.Core.Services;
using SnapCat.Infrastructure.Services;
using WpfApplication = System.Windows.Application;
using ExitEventArgs = System.Windows.ExitEventArgs;
using StartupEventArgs = System.Windows.StartupEventArgs;

namespace SnapCat.App;

public partial class App : WpfApplication
{
    public App()
    {
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SnapCat");

        SettingsStore = new JsonSettingsStore(appDataDirectory);
        HistoryStore = new JsonHistoryStore(appDataDirectory);
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
        ScreenCaptureService = new ScreenCaptureService();
        CapturedImageFileService = new CapturedImageFileService();
        GlobalHotkeyService = new GlobalHotkeyService();
        TrayIconService = new TrayIconService();
        PinnedWindowRegistryService = new PinnedWindowRegistryService();
        StartupRegistrationService = new StartupRegistrationService();
        StartupDiagnosticsService = new StartupDiagnosticsService();
        ThemeService = new ThemeService();
        CaptureActionService = new CaptureActionService(
            OcrService,
            TranslationService,
            QrCodeService,
            HistoryStore,
            CapturedImageFileService);
    }

    public ISettingsStore SettingsStore { get; }

    public IHistoryStore HistoryStore { get; }

    public IOcrService OcrService { get; }

    public IQrCodeService QrCodeService { get; }

    public ITranslationService TranslationService { get; }

    public ScreenCaptureService ScreenCaptureService { get; }

    public CapturedImageFileService CapturedImageFileService { get; }

    public GlobalHotkeyService GlobalHotkeyService { get; }

    public TrayIconService TrayIconService { get; }

    public PinnedWindowRegistryService PinnedWindowRegistryService { get; }

    public StartupRegistrationService StartupRegistrationService { get; }

    public StartupDiagnosticsService StartupDiagnosticsService { get; }

    public ThemeService ThemeService { get; }

    public CaptureActionService CaptureActionService { get; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startupSettings = SettingsStore.LoadAsync().GetAwaiter().GetResult();
        ThemeService.ApplyTheme(this, startupSettings.ThemeId);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (e.Args.Any(static arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase)))
        {
            mainWindow.StartInTray();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PinnedWindowRegistryService.CloseAllWindows();
        GlobalHotkeyService.Dispose();
        TrayIconService.Dispose();
        base.OnExit(e);
    }
}
