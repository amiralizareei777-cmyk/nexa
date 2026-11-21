using System.Speech.Synthesis;
using Microsoft.Extensions.Logging;

namespace Nexa.Windows.Speech;

/// <summary>
/// Real Windows text-to-speech using System.Speech.Synthesis. Speaks short
/// action confirmations through the installed voices using the configured
/// voice, rate, and volume.
/// </summary>
public class TextToSpeechService : ITextToSpeechService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<TextToSpeechService>? _logger;
    private readonly IReadOnlyList<string> _availableVoices;
    private readonly string? _persianVoice;
    private readonly bool _isAvailable;

    public bool IsAvailable => _isAvailable;

    public IReadOnlyList<string> AvailableVoices => _availableVoices;
    public bool SupportsPersian => _persianVoice != null;
    public Task SpeakWakeAcknowledgementAsync(CancellationToken cancellationToken = default)
        => SpeakAsync("Yes", cancellationToken);

    public TextToSpeechService(ISettingsService settings, ILogger<TextToSpeechService>? logger = null)
    {
        _settings = settings;
        _logger = logger;

        try
        {
            using var synth = new SpeechSynthesizer();
            _isAvailable = synth.GetInstalledVoices().Any(v => v.Enabled);
            _availableVoices = synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo.Name)
                .ToList();
            _persianVoice = synth.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "fa")
                ?.VoiceInfo.Name;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Text-to-speech is unavailable on this system");
            _isAvailable = false;
            _availableVoices = Array.Empty<string>();
        }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!IsAvailable)
        {
            _logger?.LogDebug("TTS unavailable; skipping speech");
            return;
        }

        try
        {
            using var synth = new SpeechSynthesizer();

            var voice = _settings.Settings.Voice.TtsVoice;
            if (_persianVoice != null && text.Any(c => c is >= '\u0600' and <= '\u06FF'))
                voice = _persianVoice;
            if (!string.IsNullOrEmpty(voice))
            {
                try { synth.SelectVoice(voice); }
                catch { /* configured voice missing; fall back to default */ }
            }

            synth.Rate = Math.Clamp(_settings.Settings.Voice.SpeechRate, -10, 10);
            synth.Volume = Math.Clamp(_settings.Settings.Voice.SpeechVolume, 0, 100);

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Prompt prompt;

            synth.SpeakCompleted += (_, e) =>
            {
                if (e.Cancelled)
                {
                    tcs.TrySetResult();
                }
                else if (e.Error != null)
                    tcs.TrySetException(e.Error);
                else
                    tcs.TrySetResult();
            };

            try
            {
                prompt = synth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
                throw;
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { synth.SpeakAsyncCancel(prompt); } catch { /* already done */ }
            });

            await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("Speech cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Text-to-speech failed");
        }
    }
}
