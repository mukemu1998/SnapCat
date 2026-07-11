using System.Net.Http;
using SnapCat.Core.Services;
using SnapCat.Infrastructure.Services;

namespace SnapCat.App.Services;

/// <summary>
/// The application composition root for runtime services.
/// Keeping construction here prevents lifecycle code from becoming a service registry.
/// </summary>
internal sealed class AppRuntimeServices
{
    private AppRuntimeServices(
        IHistoryStore historyStore,
        PinnedWindowLayoutStore pinnedWindowLayoutStore,
        IOcrService ocrService,
        IQrCodeService qrCodeService,
        ITranslationService translationService,
        TranslationSpeechService translationSpeechService,
        ScreenCaptureService screenCaptureService,
        CapturedImageFileService capturedImageFileService,
        GlobalHotkeyService globalHotkeyService,
        TrayIconService trayIconService,
        PinnedWindowRegistryService pinnedWindowRegistryService,
        StartupRegistrationService startupRegistrationService,
        StartupDiagnosticsService startupDiagnosticsService,
        IAiTaskCoordinator aiTaskCoordinator,
        IVisualPromptService visualPromptService,
        OllamaRuntimeService ollamaRuntimeService,
        GitHubReleaseUpdateService gitHubReleaseUpdateService,
        ReleaseUpdatePackageService releaseUpdatePackageService,
        CaptureActionService captureActionService)
    {
        HistoryStore = historyStore;
        PinnedWindowLayoutStore = pinnedWindowLayoutStore;
        OcrService = ocrService;
        QrCodeService = qrCodeService;
        TranslationService = translationService;
        TranslationSpeechService = translationSpeechService;
        ScreenCaptureService = screenCaptureService;
        CapturedImageFileService = capturedImageFileService;
        GlobalHotkeyService = globalHotkeyService;
        TrayIconService = trayIconService;
        PinnedWindowRegistryService = pinnedWindowRegistryService;
        StartupRegistrationService = startupRegistrationService;
        StartupDiagnosticsService = startupDiagnosticsService;
        AiTaskCoordinator = aiTaskCoordinator;
        VisualPromptService = visualPromptService;
        OllamaRuntimeService = ollamaRuntimeService;
        GitHubReleaseUpdateService = gitHubReleaseUpdateService;
        ReleaseUpdatePackageService = releaseUpdatePackageService;
        CaptureActionService = captureActionService;
    }

    public IHistoryStore HistoryStore { get; }

    public PinnedWindowLayoutStore PinnedWindowLayoutStore { get; }

    public IOcrService OcrService { get; }

    public IQrCodeService QrCodeService { get; }

    public ITranslationService TranslationService { get; }

    public TranslationSpeechService TranslationSpeechService { get; }

    public ScreenCaptureService ScreenCaptureService { get; }

    public CapturedImageFileService CapturedImageFileService { get; }

    public GlobalHotkeyService GlobalHotkeyService { get; }

    public TrayIconService TrayIconService { get; }

    public PinnedWindowRegistryService PinnedWindowRegistryService { get; }

    public StartupRegistrationService StartupRegistrationService { get; }

    public StartupDiagnosticsService StartupDiagnosticsService { get; }

    public IAiTaskCoordinator AiTaskCoordinator { get; }

    public IVisualPromptService VisualPromptService { get; }

    public OllamaRuntimeService OllamaRuntimeService { get; }

    public GitHubReleaseUpdateService GitHubReleaseUpdateService { get; }

    public ReleaseUpdatePackageService ReleaseUpdatePackageService { get; }

    public CaptureActionService CaptureActionService { get; }

    public static AppRuntimeServices Create(string appDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDataDirectory);

        var historyStore = new JsonHistoryStore(appDataDirectory);
        var pinnedWindowLayoutStore = new PinnedWindowLayoutStore(appDataDirectory);
        var tesseractCliOcrService = new TesseractCliOcrService();
        var enhancedTesseractOcrService = new EnhancedTesseractOcrService(tesseractCliOcrService);
        var windowsMediaOcrService = new WindowsMediaOcrService();
        var ocrService = new SmartOcrService(
            windowsMediaOcrService,
            enhancedTesseractOcrService,
            tesseractCliOcrService);
        var qrCodeService = new ZxingQrCodeService();
        var screenCaptureService = new ScreenCaptureService();
        var capturedImageFileService = new CapturedImageFileService();
        var translationService = new SmartTranslationService(
            new OpenAiCompatibleTranslationService(new HttpClient()),
            new LightweightWebTranslationService(new HttpClient()));
        var aiTaskCoordinator = new AiTaskCoordinator();

        return new AppRuntimeServices(
            historyStore,
            pinnedWindowLayoutStore,
            ocrService,
            qrCodeService,
            translationService,
            new TranslationSpeechService(),
            screenCaptureService,
            capturedImageFileService,
            new GlobalHotkeyService(),
            new TrayIconService(),
            new PinnedWindowRegistryService(pinnedWindowLayoutStore),
            new StartupRegistrationService(),
            new StartupDiagnosticsService(),
            aiTaskCoordinator,
            new SmartVisualPromptService(new HttpClient(), aiTaskCoordinator),
            new OllamaRuntimeService(new HttpClient()),
            new GitHubReleaseUpdateService(new HttpClient()),
            new ReleaseUpdatePackageService(new HttpClient()),
            new CaptureActionService(
                ocrService,
                translationService,
                qrCodeService,
                historyStore,
                capturedImageFileService,
                screenCaptureService));
    }
}
