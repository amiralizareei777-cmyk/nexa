using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Nexa.App.Models;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using Nexa.Windows.Shell;

namespace Nexa.App.ViewModels;

public enum VoiceState
{
    Idle,
    Listening,
    ProcessingSpeech,
    Thinking,
    Executing,
    Done,
    Failed
}

/// <summary>
/// ViewModel for the Nexa command palette. Routes natural-language commands
/// through the AI assistant, with local intent parsing as an offline fallback,
/// and handles microphone voice input plus spoken action confirmations.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly AIAssistant _assistant;
    private readonly ShellIntegration _shell;
    private readonly IAudioInputService _audio;
    private readonly ITextToSpeechService _tts;
    private readonly ISettingsService _settings;
    private string _commandText = "";
    private string _statusMessage = "";
    private bool _showStatus;
    private bool _hasResults;
    private bool _isRunning;
    private VoiceState _voiceState = VoiceState.Idle;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<(string Role, string Content)> _conversationHistory = new();

    private bool _sessionActive;
    private bool _hasPendingConfirmation;
    private string _pendingConfirmationText = "";
    private TaskCompletionSource<bool>? _confirmationSource;

    public string CommandText
    {
        get => _commandText;
        set { _commandText = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool ShowStatus
    {
        get => _showStatus;
        set { _showStatus = value; OnPropertyChanged(); }
    }

    public bool HasResults
    {
        get => _hasResults;
        set { _hasResults = value; OnPropertyChanged(); }
    }

    public VoiceState VoiceState
    {
        get => _voiceState;
        private set
        {
            if (_voiceState == value) return;
            _voiceState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsListening));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(VoiceStatusText));
        }
    }

    public bool IsListening => VoiceState is VoiceState.Listening or VoiceState.ProcessingSpeech;

    public bool MicrophoneMuted => _settings.Settings.Voice.MicrophoneMuted;

    public string ConnectionSummary
    {
        get
        {
            var ai = _settings.Settings.AI;
            var provider = ai.Provider?.ToLowerInvariant() switch
            {
                "openai" => "OpenAI",
                "anthropic" => "Claude",
                "gemini" => "Gemini",
                "openrouter" => "OpenRouter",
                "omniroute" => "OmniRoute",
                _ => "Custom provider"
            };
            return string.IsNullOrWhiteSpace(ai.Model) ? provider : $"{provider}  •  {ai.Model}";
        }
    }

    public void NotifyMicrophoneMuteChanged()
    {
        OnPropertyChanged(nameof(MicrophoneMuted));
        if (MicrophoneMuted && SessionActive)
            StopSession();
    }

    public void NotifySettingsChanged() => OnPropertyChanged(nameof(ConnectionSummary));

    /// <summary>
    /// True while an operation (voice capture or processing) is actually underway.
    /// </summary>
    public bool IsBusy => _isRunning || SessionActive;

    public bool SessionActive
    {
        get => _sessionActive;
        private set { _sessionActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); }
    }

    public bool HasPendingConfirmation
    {
        get => _hasPendingConfirmation;
        private set { _hasPendingConfirmation = value; OnPropertyChanged(); }
    }

    public string PendingConfirmationText
    {
        get => _pendingConfirmationText;
        set { _pendingConfirmationText = value; OnPropertyChanged(); }
    }

    public string VoiceStatusText => VoiceState switch
    {
        VoiceState.Idle => "",
        VoiceState.Listening => "Listening...",
        VoiceState.ProcessingSpeech => "Processing speech...",
        VoiceState.Thinking => "Thinking...",
        VoiceState.Executing => "Executing...",
        VoiceState.Done => "Done",
        VoiceState.Failed => "Voice request failed",
        _ => ""
    };

    public ObservableCollection<ResultItem> Results { get; } = new();

    /// <summary>
    /// Raised after a command finishes so the window can return focus to the input.
    /// </summary>
    public event Action? RequestCommandFocus;
    public event Action? RequestConfirmationFocus;
    public event Func<Task>? BeforeVoiceSession;
    public event Action? VoiceSessionEnded;

    public ICommand ExecuteCommand { get; }
    public ICommand VoiceCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand StopSessionCommand { get; }
    public ICommand ConfirmPendingCommand { get; }
    public ICommand DeclinePendingCommand { get; }

    public MainViewModel(
        AIAssistant assistant,
        ShellIntegration shell,
        IAudioInputService audio,
        ITextToSpeechService tts,
        ISettingsService settings)
    {
        _assistant = assistant;
        _shell = shell;
        _audio = audio;
        _tts = tts;
        _settings = settings;
        ExecuteCommand = new RelayCommand(() => _ = ExecuteAsync());
        VoiceCommand = new RelayCommand(() => _ = StartVoiceSessionAsync());
        CancelCommand = new RelayCommand(CancelCurrentOperation);
        StopSessionCommand = new RelayCommand(StopSession);
        ConfirmPendingCommand = new RelayCommand(() => _confirmationSource?.TrySetResult(true));
        DeclinePendingCommand = new RelayCommand(() => _confirmationSource?.TrySetResult(false));
    }

    internal Task ExecuteForTesting() => ExecuteAsync();

    private async Task ExecuteAsync()
    {
        var command = CommandText;
        if (string.IsNullOrWhiteSpace(command))
            return;

        if (_isRunning || SessionActive)
            return;

        _isRunning = true;
        OnPropertyChanged(nameof(IsBusy));
        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            HasResults = false;
            Results.Clear();

            // Voice flow already sets thinking/executing states; typed commands just show "Working..."
            if (VoiceState == VoiceState.Idle)
            {
                StatusMessage = "Working...";
                ShowStatus = true;
            }

            // Passing only prior exchanges as context; the current command is sent as the query.
            var priorHistory = _settings.Settings.Privacy.StoreCommandHistory
                ? new List<(string Role, string Content)>(_conversationHistory)
                : new List<(string Role, string Content)>();

            var (result, answer) = await _assistant.ProcessAsync(
                command, priorHistory, _cancellationTokenSource.Token, confirmToolCall: ConfirmToolCallAsync);

            RecordExchange(command, result, answer);

            HasResults = true;
            ShowStatus = false;
            VoiceState = VoiceState.Done;

            if (!string.IsNullOrWhiteSpace(answer))
            {
                Results.Add(new ResultItem { Title = answer });
            }
            else if (result.Data != null &&
                     result.Data.TryGetValue("files", out var files) &&
                     files is IEnumerable<FileSearchItem> fileList)
            {
                foreach (var file in fileList.Take(10))
                {
                    Results.Add(new ResultItem
                    {
                        Title = file.Name,
                        Subtitle = $"{file.Path}  •  {file.Size}  •  {file.Modified}",
                        Data = file
                    });
                }
            }
            else
            {
                Results.Add(new ResultItem
                {
                    Title = result.Message ?? "Done",
                    Subtitle = result.Success ? null : "Try another command, or open Settings to configure an AI provider."
                });
            }
            AddLinkedResults(result, answer);

            // Spoken confirmation for short action results (never long AI answers)
            if (_settings.Settings.Voice.VoiceResponses)
            {
                var toSpeak = VoiceResponseDecider.GetConfirmationToSpeak(result, answer);
                if (toSpeak != null)
                {
                    await _tts.SpeakAsync(toSpeak);
                }
            }

            AwaitStateReset();
        }
        catch (OperationCanceledException)
        {
            HasResults = true;
            ShowStatus = false;
            VoiceState = VoiceState.Idle;
            Results.Add(new ResultItem
            {
                Title = "Cancelled",
                Subtitle = "Operation was cancelled."
            });
        }
        catch (Exception ex)
        {
            HasResults = true;
            ShowStatus = false;
            VoiceState = VoiceState.Failed;
            Results.Add(new ResultItem
            {
                Title = "Something went wrong",
                Subtitle = ex.Message
            });
        }
        finally
        {
            _isRunning = false;
            OnPropertyChanged(nameof(IsBusy));
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            CommandText = "";
            RequestCommandFocus?.Invoke();
        }
    }

    /// <summary>
    /// Returns to Idle after showing "Done" so the microphone button is usable again.
    /// </summary>
    private async void AwaitStateReset()
    {
        await Task.Delay(2000);
        if (VoiceState != VoiceState.Listening)
        {
            VoiceState = VoiceState.Idle;
        }
    }

    /// <summary>
    /// Starts a persistent voice session: capture → AI → execute → repeat until
    /// the user presses Stop or the session is cancelled.
    /// </summary>
    internal async Task StartVoiceSessionAsync()
    {
        if (MicrophoneMuted)
        {
            StatusMessage = "Microphone muted. You can still type a message.";
            ShowStatus = true;
            return;
        }
        if (SessionActive || _isRunning) return;
        if (BeforeVoiceSession != null)
        {
            try { await BeforeVoiceSession(); }
            catch (Exception ex)
            {
                StatusMessage = $"Could not release the microphone: {ex.Message}";
                ShowStatus = true;
                return;
            }
        }
        if (MicrophoneMuted || SessionActive || _isRunning) return;
        SessionActive = true;
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;
        bool first = true;

        try
        {
            while (SessionActive && !MicrophoneMuted && !ct.IsCancellationRequested)
            {
                try
                {
                VoiceState = VoiceState.Listening;
                StatusMessage = first ? "Listening..." : "Listening — say your next instruction";
                ShowStatus = true;
                first = false;

                var audio = await _audio.CaptureAsync(maxWaitSeconds: 8, ct);
                if (MicrophoneMuted || !SessionActive || ct.IsCancellationRequested) break;

                if (!audio.Success || audio.WavBytes == null)
                {
                    VoiceState = VoiceState.Failed;
                    StatusMessage = audio.ErrorMessage ?? "No speech detected.";
                    ShowStatus = true;
                    await Task.Delay(1500, ct);
                    continue;  // Non-fatal — keep listening
                }

                VoiceState = VoiceState.Thinking;
                StatusMessage = "Thinking...";
                ShowStatus = true;

                var priorHistory = _settings.Settings.Privacy.StoreCommandHistory
                    ? new List<(string, string)>(_conversationHistory)
                    : new List<(string, string)>();
                var (result, answer) = await _assistant.ProcessAsync(
                    "", priorHistory, ct, audioWav: audio.WavBytes, confirmToolCall: ConfirmToolCallAsync);

                if (MicrophoneMuted || !SessionActive || ct.IsCancellationRequested) break;

                var spokenRequest = result.Data?.TryGetValue("spoken_request", out var recognized) == true
                    ? recognized?.ToString() : null;
                RecordExchange(spokenRequest ?? "[Spoken request]", result, answer);

                HasResults = true;
                ShowStatus = false;
                VoiceState = result.Success ? VoiceState.Done : VoiceState.Failed;
                Results.Clear();
                Results.Add(new ResultItem
                {
                    Title = answer ?? result.Message ?? "Done",
                    Subtitle = result.Success ? null : "The request was not completed."
                });
                AddLinkedResults(result, answer);

                if (_settings.Settings.Voice.VoiceResponses)
                {
                    var toSpeak = VoiceResponseDecider.GetConfirmationToSpeak(result, answer);
                    if (toSpeak != null)
                    {
                        await _tts.SpeakAsync(toSpeak, ct);
                        if (!SessionActive) break;
                    }
                }

                await Task.Delay(1000, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    VoiceState = VoiceState.Failed;
                    StatusMessage = $"Voice request failed: {ex.Message}";
                    ShowStatus = true;
                    await Task.Delay(1500, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful cancellation
        }
        finally
        {
            SessionActive = false;
            VoiceState = VoiceState.Idle;
            StatusMessage = "";
            ShowStatus = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            VoiceSessionEnded?.Invoke();
        }
    }

    private async Task<bool> ConfirmToolCallAsync(string toolName, Dictionary<string, object> arguments)
    {
        var cmdText = arguments.TryGetValue("command", out var c) ? c?.ToString() : "";
        PendingConfirmationText = $"Run: {cmdText}";
        HasPendingConfirmation = true;
        _confirmationSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        RequestConfirmationFocus?.Invoke();

        try
        {
            var token = _cancellationTokenSource?.Token ?? CancellationToken.None;
            return await _confirmationSource.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        }
        catch (TimeoutException)
        {
            return false;
        }
        finally
        {
            HasPendingConfirmation = false;
            PendingConfirmationText = "";
            _confirmationSource = null;
        }
    }

    private void StopSession()
    {
        _confirmationSource?.TrySetResult(false);
        _cancellationTokenSource?.Cancel();
        SessionActive = false;
    }

    private void CancelCurrentOperation()
    {
        _cancellationTokenSource?.Cancel();
        VoiceState = VoiceState.Idle;
        StatusMessage = "";
        ShowStatus = false;
    }

    public async void SelectResult(ResultItem result)
    {
        if (result.Data is FileSearchItem file)
        {
            await _shell.OpenFileAsync(file.Path);
        }
        else if (result.Data is string target)
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https")
                await _shell.OpenUrlAsync(target);
            else if (System.IO.File.Exists(target))
                await _shell.OpenFileAsync(target);
        }
    }

    private void RecordExchange(string userText, ActionResult result, string? answer)
    {
        if (!_settings.Settings.Privacy.StoreCommandHistory) return;
        _conversationHistory.Add(("user", userText));
        var outcome = result.Data?.TryGetValue("tool_log", out var log) == true
            ? log?.ToString() : result.Message;
        var content = string.IsNullOrWhiteSpace(answer) ? outcome : answer;
        if (!string.IsNullOrWhiteSpace(answer) && !string.IsNullOrWhiteSpace(outcome) &&
            outcome != "Answer from AI")
            content += $"\nActions completed: {outcome}";
        if (!string.IsNullOrWhiteSpace(content))
            _conversationHistory.Add(("assistant", content));
        if (_conversationHistory.Count > 20)
            _conversationHistory.RemoveRange(0, _conversationHistory.Count - 20);
    }

    public void ClearConversationHistory() => _conversationHistory.Clear();

    private void AddLinkedResults(ActionResult result, string? answer)
    {
        if (result.Data?.TryGetValue("path", out var pathValue) == true &&
            pathValue is string path && System.IO.File.Exists(path))
            Results.Add(new ResultItem { Title = "Open saved file", Subtitle = path, Data = path });

        if (string.IsNullOrWhiteSpace(answer)) return;
        foreach (Match match in Regex.Matches(answer, @"https?://[^\s<>\])]+", RegexOptions.IgnoreCase)
                     .Cast<Match>().Take(8))
        {
            var url = match.Value.TrimEnd('.', ',', ';');
            Results.Add(new ResultItem { Title = "Open link", Subtitle = url, Data = url });
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
