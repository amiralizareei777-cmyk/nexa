namespace Nexa.Core.Models;

/// <summary>
/// Application settings.
/// </summary>
public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public AISettings AI { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public PrivacySettings Privacy { get; set; } = new();
}

public class GeneralSettings
{
    public bool LaunchAtStartup { get; set; }
    public string GlobalHotkey { get; set; } = "Alt+Space";
    public string Theme { get; set; } = "Dark";
    public bool IsFirstRun { get; set; } = true;
}

public class AISettings
{
    public string Provider { get; set; } = "compatible";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public bool UseAIForComplexQueries { get; set; } = true;
}

public class VoiceSettings
{
    public bool MicrophoneMuted { get; set; }
    public bool WakeWordEnabled { get; set; } = true;
    public string SpeechLanguage { get; set; } = "auto";
    public bool VoiceResponses { get; set; } = true;
    public string TtsVoice { get; set; } = "";
    public int SpeechRate { get; set; } = 0;
    public int SpeechVolume { get; set; } = 100;
}

public class PrivacySettings
{
    public bool AllowScreenshotsToAI { get; set; } = true;
    public bool AllowFileNamesToAI { get; set; } = true;
    public bool StoreCommandHistory { get; set; } = true;
}
