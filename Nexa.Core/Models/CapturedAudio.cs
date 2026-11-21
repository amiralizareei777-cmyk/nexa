namespace Nexa.Core.Models;

/// <summary>
/// Raw microphone capture. WavBytes carries the full WAV file (RIFF header +
/// PCM samples) so an AI provider can do speech recognition on it.
/// </summary>
public record CapturedAudio(bool Success, byte[]? WavBytes, TimeSpan Duration, string? ErrorMessage);