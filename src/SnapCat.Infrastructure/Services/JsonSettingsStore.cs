using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SnapCat.Core.Models;
using SnapCat.Core.Services;

namespace SnapCat.Infrastructure.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
    private const string ProtectedValuePrefix = "dpapi:";
    private const int CryptProtectUiForbidden = 0x1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public JsonSettingsStore(string appDataDirectory)
    {
        Directory.CreateDirectory(appDataDirectory);
        _settingsPath = Path.Combine(appDataDirectory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var persistedSettings = await JsonSerializer.DeserializeAsync<PersistedAppSettings>(stream, SerializerOptions, cancellationToken);
        return persistedSettings?.ToAppSettings() ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(
            stream,
            PersistedAppSettings.FromAppSettings(settings),
            SerializerOptions,
            cancellationToken);
    }

    private static string Protect(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var plaintext = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectWithCurrentUserScope(plaintext);
        return ProtectedValuePrefix + Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string protectedValue, string? legacyPlaintextFallback = null)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return legacyPlaintextFallback ?? string.Empty;
        }

        if (!protectedValue.StartsWith(ProtectedValuePrefix, StringComparison.Ordinal))
        {
            return protectedValue;
        }

        try
        {
            var payload = protectedValue[ProtectedValuePrefix.Length..];
            var protectedBytes = Convert.FromBase64String(payload);
            var plaintext = UnprotectWithCurrentUserScope(protectedBytes);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException)
        {
            return legacyPlaintextFallback ?? string.Empty;
        }
        catch (CryptographicException)
        {
            return legacyPlaintextFallback ?? string.Empty;
        }
    }

    private static byte[] ProtectWithCurrentUserScope(byte[] plaintext)
    {
        var input = DataBlob.FromBytes(plaintext);

        try
        {
            if (!CryptProtectData(
                    ref input,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            try
            {
                return output.ToBytes();
            }
            finally
            {
                output.FreeFromLocalAlloc();
            }
        }
        finally
        {
            input.FreeFromHGlobal();
        }
    }

    private static byte[] UnprotectWithCurrentUserScope(byte[] protectedBytes)
    {
        var input = DataBlob.FromBytes(protectedBytes);

        try
        {
            if (!CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out var output))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            try
            {
                return output.ToBytes();
            }
            finally
            {
                output.FreeFromLocalAlloc();
            }
        }
        finally
        {
            input.FreeFromHGlobal();
        }
    }

    private sealed class PersistedAppSettings
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string ProtectedBaseUrl { get; set; } = string.Empty;

        public string ProtectedApiKey { get; set; } = string.Empty;

        public string ProtectedModel { get; set; } = string.Empty;

        public string SystemPrompt { get; set; } = AppSettings.DefaultSystemPrompt;

        public List<PersistedApiTranslationProfile> ApiProfiles { get; set; } = [];

        public string SelectedApiProfileId { get; set; } = string.Empty;

        public string TargetLanguage { get; set; } = "zh-CN";

        public string TranslationProviderPreference { get; set; } = SnapCat.Core.Models.TranslationProviderPreference.Local;

        public string OcrEngine { get; set; } = "windows-media-ocr";

        public string TesseractExecutablePath { get; set; } = string.Empty;

        public string TesseractLanguage { get; set; } = "chi_sim+eng";

        public double Temperature { get; set; } = 0.2d;

        public string HotkeyCaptureAndPin { get; set; } = "Ctrl+Shift+1";

        public string HotkeyCaptureAndTranslate { get; set; } = "Ctrl+Shift+2";

        public string HotkeyCaptureAndWaitForAction { get; set; } = "Ctrl+Shift+3";

        public string TrayLeftClickAction { get; set; } = nameof(CaptureWorkflowKind.CaptureAndWaitForAction);

        public bool LaunchAtStartup { get; set; }

        public AppSettings ToAppSettings()
        {
            var settings = new AppSettings
            {
                BaseUrl = Unprotect(ProtectedBaseUrl, BaseUrl),
                ApiKey = Unprotect(ProtectedApiKey, ApiKey),
                Model = Unprotect(ProtectedModel, Model),
                SystemPrompt = string.IsNullOrWhiteSpace(SystemPrompt) ? AppSettings.DefaultSystemPrompt : SystemPrompt,
                ApiProfiles = ApiProfiles.Select(static profile => profile.ToModel()).ToList(),
                SelectedApiProfileId = SelectedApiProfileId,
                TargetLanguage = TargetLanguage,
                TranslationProviderPreference = TranslationProviderPreference,
                OcrEngine = OcrEngine,
                TesseractExecutablePath = TesseractExecutablePath,
                TesseractLanguage = TesseractLanguage,
                Temperature = Temperature,
                HotkeyCaptureAndPin = HotkeyCaptureAndPin,
                HotkeyCaptureAndTranslate = HotkeyCaptureAndTranslate,
                HotkeyCaptureAndWaitForAction = HotkeyCaptureAndWaitForAction,
                TrayLeftClickAction = TrayLeftClickAction,
                LaunchAtStartup = LaunchAtStartup
            };

            settings.NormalizeApiProfiles();
            return settings;
        }

        public static PersistedAppSettings FromAppSettings(AppSettings settings)
        {
            var clone = new AppSettings
            {
                BaseUrl = settings.BaseUrl,
                ApiKey = settings.ApiKey,
                Model = settings.Model,
                SystemPrompt = settings.SystemPrompt,
                ApiProfiles = AppSettings.CloneApiProfiles(settings.ApiProfiles),
                SelectedApiProfileId = settings.SelectedApiProfileId,
                TargetLanguage = settings.TargetLanguage,
                TranslationProviderPreference = settings.TranslationProviderPreference,
                OcrEngine = settings.OcrEngine,
                TesseractExecutablePath = settings.TesseractExecutablePath,
                TesseractLanguage = settings.TesseractLanguage,
                Temperature = settings.Temperature,
                HotkeyCaptureAndPin = settings.HotkeyCaptureAndPin,
                HotkeyCaptureAndTranslate = settings.HotkeyCaptureAndTranslate,
                HotkeyCaptureAndWaitForAction = settings.HotkeyCaptureAndWaitForAction,
                TrayLeftClickAction = settings.TrayLeftClickAction,
                LaunchAtStartup = settings.LaunchAtStartup
            };

            clone.NormalizeApiProfiles();

            return new PersistedAppSettings
            {
                ProtectedBaseUrl = Protect(clone.BaseUrl),
                ProtectedApiKey = Protect(clone.ApiKey),
                ProtectedModel = Protect(clone.Model),
                SystemPrompt = clone.SystemPrompt,
                ApiProfiles = clone.ApiProfiles.Select(static profile => PersistedApiTranslationProfile.FromModel(profile)).ToList(),
                SelectedApiProfileId = clone.SelectedApiProfileId,
                TargetLanguage = clone.TargetLanguage,
                TranslationProviderPreference = clone.TranslationProviderPreference,
                OcrEngine = clone.OcrEngine,
                TesseractExecutablePath = clone.TesseractExecutablePath,
                TesseractLanguage = clone.TesseractLanguage,
                Temperature = clone.Temperature,
                HotkeyCaptureAndPin = clone.HotkeyCaptureAndPin,
                HotkeyCaptureAndTranslate = clone.HotkeyCaptureAndTranslate,
                HotkeyCaptureAndWaitForAction = clone.HotkeyCaptureAndWaitForAction,
                TrayLeftClickAction = clone.TrayLeftClickAction,
                LaunchAtStartup = clone.LaunchAtStartup
            };
        }
    }

    private sealed class PersistedApiTranslationProfile
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public string ProtectedBaseUrl { get; set; } = string.Empty;

        public string ProtectedApiKey { get; set; } = string.Empty;

        public string ProtectedModel { get; set; } = string.Empty;

        public string SystemPrompt { get; set; } = AppSettings.DefaultSystemPrompt;

        public ApiTranslationProfile ToModel() => new()
        {
            Id = Id,
            Name = Name,
            BaseUrl = Unprotect(ProtectedBaseUrl, BaseUrl),
            ApiKey = Unprotect(ProtectedApiKey, ApiKey),
            Model = Unprotect(ProtectedModel, Model),
            SystemPrompt = string.IsNullOrWhiteSpace(SystemPrompt) ? AppSettings.DefaultSystemPrompt : SystemPrompt
        };

        public static PersistedApiTranslationProfile FromModel(ApiTranslationProfile profile) => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            ProtectedBaseUrl = Protect(profile.BaseUrl),
            ProtectedApiKey = Protect(profile.ApiKey),
            ProtectedModel = Protect(profile.Model),
            SystemPrompt = string.IsNullOrWhiteSpace(profile.SystemPrompt) ? AppSettings.DefaultSystemPrompt : profile.SystemPrompt
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;

        public static DataBlob FromBytes(byte[] bytes)
        {
            var pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
            return new DataBlob
            {
                Size = bytes.Length,
                Data = pointer
            };
        }

        public byte[] ToBytes()
        {
            if (Size <= 0 || Data == IntPtr.Zero)
            {
                return Array.Empty<byte>();
            }

            var bytes = new byte[Size];
            Marshal.Copy(Data, bytes, 0, Size);
            return bytes;
        }

        public void FreeFromHGlobal()
        {
            if (Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Data);
                Data = IntPtr.Zero;
                Size = 0;
            }
        }

        public void FreeFromLocalAlloc()
        {
            if (Data != IntPtr.Zero)
            {
                _ = LocalFree(Data);
                Data = IntPtr.Zero;
                Size = 0;
            }
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr dataDescription,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
