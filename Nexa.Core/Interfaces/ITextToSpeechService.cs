namespace Nexa.Core.Interfaces;

public interface ITextToSpeechService
{
    bool IsAvailable { get; }

    /// <summary>
    /// Names of installed Windows text-to-speech voices.
    /// </summary>
    IReadOnlyList<string> AvailableVoices { get; }

    /// <summary>
    /// Speaks the given text using the configured voice and rate. Resolves
    /// voice, language, and rate from the current settings.
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
}