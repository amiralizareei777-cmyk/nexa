using Nexa.Core.Models;

namespace Nexa.Core.Interfaces;

/// <summary>
/// Captures raw microphone audio. No speech recognition happens in the app —
/// the captured WAV is sent to an AI provider that does the recognition and
/// reasoning in one multimodal request.
/// </summary>
public interface IAudioInputService
{
    bool IsMicrophoneAvailable { get; }

    /// <summary>
    /// Listens to the default microphone until the user stops speaking and
    /// returns the full utterance as WAV bytes, or times out / is cancelled.
    /// </summary>
    /// <param name="maxWaitSeconds">How long to wait for speech to start before giving up.</param>
    Task<CapturedAudio> CaptureAsync(int maxWaitSeconds = 8, CancellationToken cancellationToken = default);
}