using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Nexa.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly AIProviderFactory _providerFactory;
    private readonly IAudioInputService _audio;
    private readonly ITextToSpeechService _tts;
    private readonly System.Windows.Media.Brush _secondaryBrush;
    private readonly System.Windows.Media.Brush _successBrush = System.Windows.Media.Brushes.LimeGreen;
    private readonly System.Windows.Media.Brush _errorBrush = System.Windows.Media.Brushes.OrangeRed;
    private readonly Dictionary<string, (string BaseUrl, string Model)> _providerFields =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedProvider;
    private CancellationTokenSource? _microphoneTestCancellation;

    public SettingsWindow(
        ISettingsService settingsService,
        ISecretStore secretStore,
        AIProviderFactory providerFactory,
        IAudioInputService audio,
        ITextToSpeechService tts)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _secretStore = secretStore;
        _providerFactory = providerFactory;
        _audio = audio;
        _tts = tts;
        _secondaryBrush = FindResource("SecondaryTextBrush") as System.Windows.Media.Brush
                          ?? System.Windows.Media.Brushes.Gray;
        Loaded += SettingsWindow_Loaded;
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = _settingsService.Settings;

        // General
        GlobalHotkeyTextBox.Text = settings.General.GlobalHotkey;
        SelectTag(ThemeComboBox, settings.General.Theme);
        LaunchAtStartupCheckBox.IsChecked = settings.General.LaunchAtStartup;

        // AI
        SelectTag(ProviderComboBox, settings.AI.Provider);
        BaseUrlTextBox.Text = settings.AI.BaseUrl;
        ModelComboBox.Text = settings.AI.Model;
        UseAIForComplexQueriesCheckBox.IsChecked = settings.AI.UseAIForComplexQueries;

        // Voice
        WakeWordCheckBox.IsChecked = settings.Voice.WakeWordEnabled;
        SelectTag(SpeechLanguageComboBox, settings.Voice.SpeechLanguage);
        VoiceResponsesCheckBox.IsChecked = settings.Voice.VoiceResponses;
        SpeechRateSlider.Value = settings.Voice.SpeechRate;
        SpeechVolumeSlider.Value = settings.Voice.SpeechVolume;

        foreach (var voice in _tts.AvailableVoices)
        {
            TtsVoiceComboBox.Items.Add(new ComboBoxItem { Content = voice, Tag = voice });
        }
        if (TtsVoiceComboBox.Items.Count > 0)
        {
            SelectTag(TtsVoiceComboBox, settings.Voice.TtsVoice);
            if (TtsVoiceComboBox.SelectedIndex < 0)
                TtsVoiceComboBox.SelectedIndex = 0;
        }
        else
        {
            TtsVoiceComboBox.Items.Add(new ComboBoxItem { Content = "Default Voice", Tag = "" });
            TtsVoiceComboBox.SelectedIndex = 0;
        }

        // Privacy
        AllowScreenshotsCheckBox.IsChecked = settings.Privacy.AllowScreenshotsToAI;
        AllowFileNamesCheckBox.IsChecked = settings.Privacy.AllowFileNamesToAI;
        StoreCommandHistoryCheckBox.IsChecked = settings.Privacy.StoreCommandHistory;

        UpdateBaseUrlVisibility();
        UpdateProviderHelp();
        await UpdateSavedKeyStatusAsync();
    }

    public void ShowGuide() => GuideTab.IsChecked = true;

    private void OpenAISettings_Click(object sender, RoutedEventArgs e) => AITab.IsChecked = true;

    internal void CancelMicrophoneTest() => _microphoneTestCancellation?.Cancel();

    private async void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var provider = GetSelectedProviderTag();
        if (provider is not null && _selectedProvider is not null && provider != _selectedProvider)
        {
            ApiKeyBox.Clear();
            _providerFields[_selectedProvider] = (BaseUrlTextBox.Text, ModelComboBox.Text);
            var fields = _providerFields.TryGetValue(provider, out var previous)
                ? previous
                : (AIProviderFactory.DefaultBaseUrl(provider), DefaultModel(provider));
            BaseUrlTextBox.Text = fields.Item1;
            ModelComboBox.Items.Clear();
            ModelComboBox.Text = fields.Item2;
        }
        _selectedProvider = provider;
        UpdateBaseUrlVisibility();
        UpdateProviderHelp();
        await UpdateSavedKeyStatusAsync();
    }

    private async Task UpdateSavedKeyStatusAsync()
    {
        if (SavedKeyStatusBlock == null) return;
        var provider = GetSelectedProviderTag();
        if (provider == null) return;
        var exists = await _secretStore.SecretExistsAsync(ProviderKeyStore.KeyFor(provider));
        if (provider == GetSelectedProviderTag())
            SavedKeyStatusBlock.Text = exists
                ? "A key is saved for this provider. Leave the box empty to keep it."
                : "No key is saved for this provider yet.";
    }

    private void UpdateProviderHelp()
    {
        if (ProviderHelpBlock == null || OpenProviderKeyButton == null) return;
        var (hint, url) = GetProviderHelp(GetSelectedProviderTag());
        ProviderHelpBlock.Text = hint;
        OpenProviderKeyButton.Tag = url;
        OpenProviderKeyButton.Visibility = url == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static (string Hint, string? Url) GetProviderHelp(string? provider) => provider switch
    {
        "gemini" => ("Easy first try: create a key in Google AI Studio. Some models have a limited free tier; choose an audio-capable model for voice.", "https://aistudio.google.com/app/apikey"),
        "openrouter" => ("Create a key in OpenRouter Keys. Free models have rate limits, and only some models accept audio input.", "https://openrouter.ai/settings/keys"),
        "openai" => ("Create a key in OpenAI Platform API Keys. API billing is separate from a ChatGPT subscription.", "https://platform.openai.com/api-keys"),
        "anthropic" => ("Create a key in Claude Console Settings → API Keys. Direct Claude in Nexa supports typed commands.", "https://platform.claude.com/settings/keys"),
        "omniroute" => ("Run OmniRoute locally, connect an upstream provider in its dashboard, then create a client key in API Manager / API Keys.", "http://localhost:20128"),
        _ => ("Use a trusted OpenAI-compatible endpoint. Get its Base URL, API key and model name from that endpoint's own documentation.", null)
    };

    private void OpenProviderKey_Click(object sender, RoutedEventArgs e)
        => OpenGuideLink(OpenProviderKeyButton.Tag as string);

    private void GuideLink_Click(object sender, RoutedEventArgs e)
        => OpenGuideLink((sender as System.Windows.Controls.Button)?.Tag as string);

    private void OpenGuideLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https")) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus($"Could not open the page: {ex.Message}", _errorBrush);
        }
    }

    private void UpdateBaseUrlVisibility()
    {
        var tag = GetSelectedProviderTag();
        bool needsBaseUrl = tag is "compatible" or "omniroute" or "openrouter" or "gemini";
        BaseUrlLabel.Visibility = needsBaseUrl ? Visibility.Visible : Visibility.Collapsed;
        BaseUrlTextBox.Visibility = needsBaseUrl ? Visibility.Visible : Visibility.Collapsed;

        // Prefill default base URL when empty
        if (tag == "openai")
        {
            BaseUrlTextBox.Text = AIProviderFactory.DefaultBaseUrl(tag);
        }
        else if (string.IsNullOrWhiteSpace(BaseUrlTextBox.Text))
        {
            BaseUrlTextBox.Text = AIProviderFactory.DefaultBaseUrl(tag ?? "compatible");
        }

        if (string.IsNullOrWhiteSpace(ModelComboBox.Text))
        {
            ModelComboBox.Text = DefaultModel(tag);
        }
    }

    private static string DefaultModel(string? provider) => provider switch
    {
        "openai" => "gpt-4o-mini",
        "anthropic" => "claude-sonnet-5",
        "gemini" => "gemini-2.0-flash",
        "openrouter" => "openai/gpt-4o-mini",
        "omniroute" => "llama-3.1-8b-instruct",
        _ => ""
    };

    private async void RefreshModels_Click(object sender, RoutedEventArgs e)
    {
        ShowStatus("Refreshing models...", _secondaryBrush);
        try
        {
            await ApplyAITempSettingsAsync();
            var modelList = await _providerFactory.CreateProvider().ListModelsAsync();

            var previous = ModelComboBox.Text;
            ModelComboBox.Items.Clear();
            foreach (var model in modelList)
            {
                ModelComboBox.Items.Add(model);
            }
            ModelComboBox.Text = previous;

            ShowStatus($"Loaded {modelList.Count} models.", _secondaryBrush);
        }
        catch (NotSupportedException)
        {
            ShowStatus("Model discovery isn't supported for this provider. Type a model name manually.", _secondaryBrush);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to refresh models: {ex.Message}", _errorBrush);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        ShowStatus("Testing connection...", _secondaryBrush);
        try
        {
            await ApplyAITempSettingsAsync();
            var result = await _providerFactory.CreateProvider().TestConnectionAsync();
            ShowStatus(result.Message, result.Success ? _successBrush : _errorBrush);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", _errorBrush);
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = _settingsService.Settings;

            // General
            settings.General.Theme = GetSelectedTag(ThemeComboBox) ?? "Dark";
            settings.General.LaunchAtStartup = LaunchAtStartupCheckBox.IsChecked == true;

            // AI
            settings.AI.Provider = GetSelectedProviderTag() ?? "compatible";
            settings.AI.BaseUrl = BaseUrlTextBox.Text.Trim();
            settings.AI.Model = ModelComboBox.Text?.Trim() ?? "";
            settings.AI.UseAIForComplexQueries = UseAIForComplexQueriesCheckBox.IsChecked == true;

            // API key — stored in the Windows Credential Manager, never in settings.json
            if (ApiKeyBox.SecurePassword.Length > 0)
            {
                var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(ApiKeyBox.SecurePassword);
                try
                {
                    var key = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr, ApiKeyBox.SecurePassword.Length);
                    if (!string.IsNullOrEmpty(key))
                    {
                        await _secretStore.StoreSecretAsync(ProviderKeyStore.KeyFor(settings.AI.Provider), key);
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
                ApiKeyBox.Clear();
            }

            // Voice
            settings.Voice.WakeWordEnabled = WakeWordCheckBox.IsChecked == true;
            settings.Voice.SpeechLanguage = GetSelectedTag(SpeechLanguageComboBox) ?? "auto";
            settings.Voice.VoiceResponses = VoiceResponsesCheckBox.IsChecked == true;
            settings.Voice.TtsVoice = (TtsVoiceComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
            settings.Voice.SpeechRate = (int)SpeechRateSlider.Value;
            settings.Voice.SpeechVolume = (int)SpeechVolumeSlider.Value;

            // Privacy
            settings.Privacy.AllowScreenshotsToAI = AllowScreenshotsCheckBox.IsChecked == true;
            settings.Privacy.AllowFileNamesToAI = AllowFileNamesCheckBox.IsChecked == true;
            settings.Privacy.StoreCommandHistory = StoreCommandHistoryCheckBox.IsChecked == true;
            if (!settings.Privacy.StoreCommandHistory)
                ((App)System.Windows.Application.Current).ClearConversationHistory();

            await _settingsService.SaveAsync();
            await UpdateSavedKeyStatusAsync();
            var app = (App)System.Windows.Application.Current;
            app.ApplyTheme(settings.General.Theme);
            app.ApplyLaunchAtStartup(settings.General.LaunchAtStartup);
            app.NotifySettingsChanged();
            await app.RefreshWakeWordListenerAsync();

            ShowStatus("Settings saved successfully.", _successBrush);
        }
        catch (Exception ex)
        {
            ShowStatus($"Failed to save settings: {ex.Message}", _errorBrush);
        }
    }

    private async void TestMicrophone_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsService.Settings.Voice.MicrophoneMuted)
        {
            VoiceStatusBlock.Text = "Microphone is muted. Unmute it in Nexa to run this test.";
            return;
        }
        using var captureCancellation = new CancellationTokenSource();
        _microphoneTestCancellation = captureCancellation;
        VoiceStatusBlock.Text = "Testing microphone...";
        try
        {
            await ((App)System.Windows.Application.Current).PauseWakeListenerAsync();
            if (_settingsService.Settings.Voice.MicrophoneMuted || captureCancellation.IsCancellationRequested)
            {
                VoiceStatusBlock.Text = "Microphone test cancelled because mute is on.";
                return;
            }
            var audio = await _audio.CaptureAsync(maxWaitSeconds: 5, captureCancellation.Token);
            if (_settingsService.Settings.Voice.MicrophoneMuted || captureCancellation.IsCancellationRequested)
            {
                VoiceStatusBlock.Text = "Microphone test cancelled because mute is on.";
                return;
            }

            if (!audio.Success || audio.WavBytes == null)
            {
                VoiceStatusBlock.Text = audio.ErrorMessage ?? "No speech detected.";
                return;
            }

            // Try to transcribe via AI provider if configured
            try
            {
                var provider = _providerFactory.CreateProvider();
                var response = await provider.ProcessQueryAsync(
                    "Transcribe the audio and respond with only the transcribed text.",
                    Array.Empty<AITool>(),
                    conversationHistory: null,
                    cancellationToken: default,
                    audioWav: audio.WavBytes);

                if (!string.IsNullOrWhiteSpace(response.TextResponse))
                {
                    VoiceStatusBlock.Text = $"Microphone works. Recognized: \"{response.TextResponse}\"";
                }
                else
                {
                    VoiceStatusBlock.Text = "Captured audio successfully, but AI did not return a transcription.";
                }
            }
            catch (NotSupportedException ex)
            {
                VoiceStatusBlock.Text = $"Microphone works. {ex.Message}";
            }
            catch (Exception ex)
            {
                VoiceStatusBlock.Text = $"Microphone works, but AI transcription failed: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            VoiceStatusBlock.Text = $"Microphone test failed: {ex.Message}";
        }
        finally
        {
            _microphoneTestCancellation = null;
            ((App)System.Windows.Application.Current).ResumeWakeListener();
        }
    }

    private async void TestVoice_Click(object sender, RoutedEventArgs e)
    {
        VoiceStatusBlock.Text = "Speaking...";
        await _tts.SpeakAsync("Hello. Nexa voice is working.");
        VoiceStatusBlock.Text = "Voice test complete.";
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).ClearConversationHistory();
        ShowStatus("Recent conversation context cleared.", _secondaryBrush);
    }

    private async Task ApplyAITempSettingsAsync()
    {
        // Apply the current UI values so the factory-created provider reads them
        var settings = _settingsService.Settings;
        settings.AI.Provider = GetSelectedProviderTag() ?? "compatible";
        settings.AI.BaseUrl = BaseUrlTextBox.Text.Trim();
        settings.AI.Model = ModelComboBox.Text?.Trim() ?? "";

        // Stage a freshly typed API key so Test Connection / Refresh Models use it live.
        if (ApiKeyBox.SecurePassword.Length > 0)
        {
            var ptr = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(ApiKeyBox.SecurePassword);
            try
            {
                var key = System.Runtime.InteropServices.Marshal.PtrToStringUni(ptr, ApiKeyBox.SecurePassword.Length);
                if (!string.IsNullOrEmpty(key))
                {
                    await _secretStore.StoreSecretAsync(ProviderKeyStore.KeyFor(settings.AI.Provider), key);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }

    private void ShowStatus(string message, System.Windows.Media.Brush brush)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = brush;
    }

    private string? GetSelectedProviderTag()
        => GetSelectedTag(ProviderComboBox);

    private static string? GetSelectedTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void SelectTag(ComboBox comboBox, string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return;
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem ci && string.Equals(ci.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = ci;
                return;
            }
        }
    }
}
