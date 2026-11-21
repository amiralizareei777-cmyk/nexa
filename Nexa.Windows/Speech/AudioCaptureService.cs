using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Windows.Speech;

/// <summary>
/// Captures a single spoken utterance from the default microphone as a WAV file
/// using NAudio. Simple amplitude-based voice activity detection ends the
/// recording after a trailing silence; speech recognition itself is delegated
/// to an AI provider, so this class never transcribes anything.
/// </summary>
public sealed class AudioCaptureService : IAudioInputService
{
    private const int SampleRate = 16000;
    private const int BitsPerSample = 16;
    private static readonly WaveFormat CaptureFormat = new(SampleRate, BitsPerSample, 1);

    /// <summary>RMS threshold above which the signal is treated as speech.</summary>
    private const double SpeechThreshold = 0.02;
    /// <summary>Trailing silence after which the utterance is considered finished.</summary>
    private const int SpeechEndSilenceMs = 800;
    /// <summary>Hard cap on a single utterance.</summary>
    private const double MaxUtteranceMs = 60_000;

    private readonly ILogger<AudioCaptureService>? _logger;

    public AudioCaptureService(ILogger<AudioCaptureService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsMicrophoneAvailable
    {
        get
        {
            try { return WaveInEvent.DeviceCount > 0; }
            catch { return false; }
        }
    }

    public async Task<CapturedAudio> CaptureAsync(int maxWaitSeconds = 8, CancellationToken cancellationToken = default)
    {
        if (!IsMicrophoneAvailable)
        {
            return new CapturedAudio(false, null, TimeSpan.Zero,
                "No microphone found. Connect a microphone and check Windows privacy settings.");
        }

        var pcm = new List<byte>();
        var tcs = new TaskCompletionSource<CapturedAudio>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var waveIn = new WaveInEvent
        {
            WaveFormat = CaptureFormat,
            BufferMilliseconds = 100,
            DeviceNumber = 0
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var speechActive = false;
        var speechDetected = false;
        var lastSpeechTime = TimeSpan.Zero;
        long speechStartedMs = -1;
        bool cancelled = false;

        waveIn.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded <= 0) return;

            var elapsed = sw.Elapsed;
            var rms = ComputeRms(e.Buffer, e.BytesRecorded);

            if (rms >= SpeechThreshold)
            {
                speechActive = true;
                speechDetected = true;
                lastSpeechTime = elapsed;
                if (speechStartedMs < 0) speechStartedMs = (long)elapsed.TotalMilliseconds;
                pcm.AddRange(e.Buffer.Take(e.BytesRecorded));
            }
            else if (speechActive)
            {
                // Keep trailing silence so the model hears a clean end-of-utterance.
                pcm.AddRange(e.Buffer.Take(e.BytesRecorded));
                if (elapsed - lastSpeechTime >= TimeSpan.FromMilliseconds(SpeechEndSilenceMs))
                    StopQuietly();
            }

            if (speechStartedMs >= 0 && elapsed.TotalMilliseconds - speechStartedMs >= MaxUtteranceMs)
                StopQuietly();
        };

        waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception != null)
            {
                tcs.TrySetResult(new CapturedAudio(false, null, TimeSpan.Zero, $"Microphone error: {e.Exception.Message}"));
            }
            else if (cancelled)
            {
                tcs.TrySetResult(new CapturedAudio(false, null, TimeSpan.Zero, "Listening cancelled."));
            }
            else if (!speechDetected || pcm.Count == 0)
            {
                tcs.TrySetResult(new CapturedAudio(false, null, TimeSpan.Zero, "No speech detected. Try again."));
            }
            else
            {
                tcs.TrySetResult(new CapturedAudio(true, WavBuilder.ToWav(pcm), sw.Elapsed, null));
            }
        };

        void StopQuietly()
        {
            try { waveIn.StopRecording(); } catch { }
        }

        try
        {
            waveIn.StartRecording();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "Microphone permission denied");
            return new CapturedAudio(false, null, TimeSpan.Zero,
                "Microphone permission denied. Allow Nexa to use the microphone in Windows settings.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not start microphone capture");
            return new CapturedAudio(false, null, TimeSpan.Zero, $"Microphone not available: {ex.Message}");
        }

        using (cancellationToken.Register(() =>
        {
            cancelled = true;
            StopQuietly();
        }))
        {
            var waitBeforeSpeech = TimeSpan.FromSeconds(Math.Max(1, maxWaitSeconds));

            var wonTcs = await Task.WhenAny(tcs.Task, Task.Delay(waitBeforeSpeech, cancellationToken)) == tcs.Task;

            if (cancellationToken.IsCancellationRequested)
            {
                return new CapturedAudio(false, null, TimeSpan.Zero, "Listening cancelled.");
            }

            if (!wonTcs && !speechDetected)
            {
                // No speech within the wait window — stop and report.
                StopQuietly();
                return await Task.WhenAny(tcs.Task, Task.Delay(2000)) == tcs.Task
                    ? tcs.Task.Result
                    : new CapturedAudio(false, null, TimeSpan.Zero, "No speech detected. Try again.");
            }

            // Speech is underway (or just finished) — wait for the utterance to complete:
            // VAD silence, the utterance cap, or user cancellation will stop recording.
            var complete = await Task.WhenAny(
                tcs.Task,
                Task.Delay(TimeSpan.FromMilliseconds(MaxUtteranceMs) + TimeSpan.FromSeconds(10), cancellationToken));

            if (complete == tcs.Task) return tcs.Task.Result;

            return cancelled
                ? new CapturedAudio(false, null, TimeSpan.Zero, "Listening cancelled.")
                : new CapturedAudio(false, null, TimeSpan.Zero, "No speech detected. Try again.");
        }
    }

    private static double ComputeRms(byte[] buffer, int count)
    {
        if (count < 2) return 0;
        double sumSquares = 0;
        int samples = count / 2;
        for (int i = 0; i < samples; i++)
        {
            short sample = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
            double v = sample;
            sumSquares += v * v;
        }
        return Math.Sqrt(sumSquares / samples) / 32768.0;
    }
}

/// <summary>
/// Builds a WAV container (44-byte RIFF header) around raw 16-bit PCM samples.
/// Pure and hardware-free so it can be unit-tested.
/// </summary>
internal static class WavBuilder
{
    public static byte[] ToWav(IReadOnlyList<byte> pcm)
    {
        var dataLen = pcm.Count;
        var wav = new byte[44 + dataLen];
        WriteAscii(wav, 0, "RIFF");
        WriteInt32(wav, 4, 36 + dataLen);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt32(wav, 16, 16);          // fmt chunk size
        WriteInt16(wav, 20, 1);           // PCM
        WriteInt16(wav, 22, 1);           // channel count
        WriteInt32(wav, 24, 16000);       // sample rate
        WriteInt32(wav, 28, 16000 * 2);   // byte rate
        WriteInt16(wav, 32, 2);           // block align
        WriteInt16(wav, 34, 16);          // bits per sample
        WriteAscii(wav, 36, "data");
        WriteInt32(wav, 40, dataLen);
        for (int i = 0; i < dataLen; i++) wav[44 + i] = pcm[i];
        return wav;
    }

    private static void WriteAscii(byte[] dest, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++) dest[offset + i] = (byte)text[i];
    }

    private static void WriteInt32(byte[] dest, int offset, int value)
    {
        dest[offset] = (byte)value;
        dest[offset + 1] = (byte)(value >> 8);
        dest[offset + 2] = (byte)(value >> 16);
        dest[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] dest, int offset, int value)
    {
        dest[offset] = (byte)value;
        dest[offset + 1] = (byte)(value >> 8);
    }
}
