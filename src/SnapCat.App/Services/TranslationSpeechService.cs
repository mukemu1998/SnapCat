using System.Runtime.InteropServices;

namespace SnapCat.App.Services;

public sealed class TranslationSpeechService
{
    private const int SpeakAsyncAndPurge = 3;
    private object? _voice;

    public void Stop()
    {
        if (_voice is null)
        {
            return;
        }

        try
        {
            ((dynamic)_voice).Speak(string.Empty, SpeakAsyncAndPurge);
        }
        catch
        {
            // Closing a popup must remain reliable even when SAPI has already stopped.
        }
    }

    public string Speak(string? text, string languageCode)
    {
        var content = text?.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return "没有可朗读的文本。";
        }

        try
        {
            var voice = GetVoice();
            TrySelectVoice(voice, languageCode);
            voice.Speak(content, SpeakAsyncAndPurge);
            return $"正在朗读：{TranslationLanguageHelper.GetLanguageLabel(languageCode)}。";
        }
        catch (COMException ex)
        {
            return $"朗读失败：系统语音组件不可用（{ex.Message}）。";
        }
        catch (Exception ex)
        {
            return $"朗读失败：{ex.Message}";
        }
    }

    private dynamic GetVoice()
    {
        if (_voice is not null)
        {
            return _voice;
        }

        var voiceType = Type.GetTypeFromProgID("SAPI.SpVoice")
            ?? throw new InvalidOperationException("未找到 Windows SAPI 语音组件。");
        _voice = Activator.CreateInstance(voiceType)
            ?? throw new InvalidOperationException("无法创建 Windows SAPI 语音组件。");
        return _voice;
    }

    private static void TrySelectVoice(dynamic voice, string languageCode)
    {
        var lcid = GetLanguageLcid(languageCode);
        if (string.IsNullOrWhiteSpace(lcid))
        {
            return;
        }

        try
        {
            var voices = voice.GetVoices($"Language={lcid}");
            if (voices.Count > 0)
            {
                voice.Voice = voices.Item(0);
            }
        }
        catch
        {
            // 没有安装对应语言语音时，交给系统默认语音朗读。
        }
    }

    private static string GetLanguageLcid(string languageCode)
    {
        return languageCode switch
        {
            TranslationLanguageHelper.ChineseSimplified => "804",
            TranslationLanguageHelper.English => "409",
            TranslationLanguageHelper.Japanese => "411",
            TranslationLanguageHelper.Korean => "412",
            TranslationLanguageHelper.Vietnamese => "42A",
            TranslationLanguageHelper.French => "40C",
            TranslationLanguageHelper.German => "407",
            TranslationLanguageHelper.Russian => "419",
            _ => string.Empty
        };
    }
}
