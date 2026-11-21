using System.Speech.Recognition;
using Microsoft.Extensions.Logging;

namespace Nexa.Windows.Speech;

/// <summary>Uses the installed Windows recognizer for a local, continuous Nexa wake word.</summary>
public sealed class WakeWordListener : IDisposable
{
    private readonly ILogger<WakeWordListener>? _logger;
    private readonly object _sync = new();
    private SpeechRecognitionEngine? _engine;
    private TaskCompletionSource? _completed;
    private bool _disposed;

    public event Action? WakeWordDetected;

    public WakeWordListener(ILogger<WakeWordListener>? logger = null) => _logger = logger;

    public bool TryStart()
    {
        lock (_sync)
        {
            if (_disposed) return false;
            if (_engine != null) return true;
            try
            {
                var installed = SpeechRecognitionEngine.InstalledRecognizers()
                    .FirstOrDefault(r => r.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                    ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();
                if (installed == null) return false;

                var engine = new SpeechRecognitionEngine(installed);
                var grammarBuilder = new GrammarBuilder("Nexa") { Culture = installed.Culture };
                engine.LoadGrammar(new Grammar(grammarBuilder));
                engine.SpeechRecognized += OnSpeechRecognized;
                engine.RecognizeCompleted += OnRecognizeCompleted;
                engine.SetInputToDefaultAudioDevice();
                _completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _engine = engine;
                engine.RecognizeAsync(RecognizeMode.Multiple);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not start local Nexa wake word listener");
                _engine?.Dispose();
                _engine = null;
                _completed = null;
                return false;
            }
        }
    }

    public async Task PauseAsync()
    {
        SpeechRecognitionEngine? engine;
        Task? completed;
        lock (_sync)
        {
            engine = _engine;
            completed = _completed?.Task;
            _engine = null;
            if (engine == null) return;
            engine.SpeechRecognized -= OnSpeechRecognized;
            try { engine.RecognizeAsyncCancel(); }
            catch (InvalidOperationException) { /* already finished */ }
        }

        if (completed != null)
            await Task.WhenAny(completed, Task.Delay(1500));
        lock (_sync)
        {
            if (_completed?.Task == completed) _completed = null;
        }
        engine.RecognizeCompleted -= OnRecognizeCompleted;
        engine.Dispose();
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result?.Text.Equals("Nexa", StringComparison.OrdinalIgnoreCase) == true &&
            e.Result.Confidence >= 0.45f)
            WakeWordDetected?.Invoke();
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        lock (_sync) _completed?.TrySetResult();
    }

    public void Dispose()
    {
        _disposed = true;
        _ = PauseAsync();
    }
}
