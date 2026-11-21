using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Nexa.App.Models;
using Nexa.App.ViewModels;
using Nexa.Core.AI;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using Nexa.Core.Parsing;
using Nexa.Windows.Shell;

namespace Nexa.Tests;

/// <summary>
/// Tests for awareness of conversation continuity across commands:
/// input clears after each command, conversations stay active, and
/// cancellation does not terminate the session.
/// </summary>
public class CommandConversationTests
{
    private readonly Mock<IIntentParser> _parser = new();
    private readonly ActionRouter _router;
    private readonly Mock<ISettingsService> _settings = new();
    private readonly Mock<ISecretStore> _secrets = new();
    private readonly Mock<IAudioInputService> _audio = new();
    private readonly Mock<ITextToSpeechService> _tts = new();
    private readonly Mock<IAction> _openAppAction = new();

    public CommandConversationTests()
    {
        _router = new ActionRouter(_parser.Object);
        _openAppAction.Setup(a => a.Name).Returns("open_application");
        _openAppAction.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.Safe);
        _openAppAction.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        _openAppAction.Setup(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Dictionary<string, object> parameters, CancellationToken _) =>
            {
                var name = parameters.TryGetValue("app_name", out var v) ? v?.ToString() : "";
                return ActionResult.Successful(name == "Notepad" ? "Opened Notepad" : "Opened Google Chrome");
            });
        _router.RegisterAction(_openAppAction.Object);
    }

    private static AppSettings TestSettings() => new()
    {
        AI = new AISettings
        {
            Provider = "compatible",
            BaseUrl = "http://localhost:20128/v1",
            Model = "model-x",
            UseAIForComplexQueries = true
        },
        Voice = new VoiceSettings
        {
            VoiceResponses = false,
            SpeechLanguage = "en-US",
            SpeechRate = 1,
            SpeechVolume = 100
        }
    };

    private AIAssistant BuildAssistant()
    {
        var settings = TestSettings();
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("test-key");
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var http = FakeHttpMessageHandler.Client("""{"choices":[{"message":{"content":"Done."}}]}""");
        var factory = new AIProviderFactory(_settings.Object, _secrets.Object, NullLoggerFactory.Instance, http);
        return new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);
    }

    private MainViewModel BuildViewModel(AIAssistant assistant)
    {
        _settings.SetupGet(s => s.Settings).Returns(TestSettings());
        return new MainViewModel(
            assistant,
            new ShellIntegration(),
            _audio.Object,
            _tts.Object,
            _settings.Object);
    }

    [Fact]
    public void CommandText_DefaultsToEmpty_SoPlaceholderIsVisible()
    {
        // The window shows a placeholder overlay while CommandText is empty
        // (Visibility driven by the bound TextBox). An empty CommandText means
        // the placeholder is visible on launch and nothing is submitted.
        var vm = new MainViewModel(null!, null!, _audio.Object, _tts.Object, _settings.Object);

        vm.CommandText.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void CommandText_SetToRecognizedSpeech_ExposesTextToBinding()
    {
        var vm = new MainViewModel(null!, null!, _audio.Object, _tts.Object, _settings.Object);
        // Local commands short-circuit the assistant, so a null assistant is fine.
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>()))
            .Returns(new Intent { ActionName = "open_application", Parameters = new Dictionary<string, object> { ["app_name"] = "Chrome" } });

        var notified = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CommandText)) notified = true;
        };

        vm.CommandText = "Open Chrome";

        notified.Should().BeTrue();
        vm.CommandText.Should().Be("Open Chrome");
    }

    [Fact]
    public void CommandText_ClearedToEmpty_RaisesNotification()
    {
        var vm = new MainViewModel(null!, null!, _audio.Object, _tts.Object, _settings.Object);
        vm.CommandText = "Set volume 30";
        var notified = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CommandText)) notified = true;
        };

        vm.CommandText = "";

        notified.Should().BeTrue();
        vm.CommandText.Should().BeEmpty();
    }

    [Fact]
    public async Task VoiceSession_ProcessesAudioAndWaitsForStop()
    {
        var processed = 0;
        var provider = new CapturingProvider(_ => processed++);
        _settings.SetupGet(s => s.Settings).Returns(TestSettings());
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var assistant = new AIAssistant(_router, new StubProviderFactory(provider),
            _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);
        var vm = BuildViewModel(assistant);
        var captures = 0;
        _audio.Setup(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken _) =>
            {
                captures++;
                if (captures == 3)
                    vm.StopSessionCommand.Execute(null);
                return Task.FromResult(new CapturedAudio(true, new byte[] { 1, 2, 3 },
                    TimeSpan.FromSeconds(1), null));
            });

        await vm.StartVoiceSessionAsync();

        captures.Should().Be(3);
        processed.Should().Be(2);
        vm.SessionActive.Should().BeFalse();
        vm.VoiceState.Should().Be(VoiceState.Idle);
        _parser.Verify(p => p.TryParseLocal(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VoiceSession_StopsWhileWaitingForAudio()
    {
        var vm = BuildViewModel(BuildAssistant());
        var captureStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _audio.Setup(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken ct) =>
            {
                captureStarted.TrySetResult(ct);
                return Task.Delay(Timeout.Infinite, ct)
                    .ContinueWith(_ => new CapturedAudio(false, null, TimeSpan.Zero, "Stopped"),
                        TaskScheduler.Default);
            });

        var session = vm.StartVoiceSessionAsync();
        var token = await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.StopSessionCommand.Execute(null);
        await session.WaitAsync(TimeSpan.FromSeconds(2));

        token.IsCancellationRequested.Should().BeTrue();
        vm.SessionActive.Should().BeFalse();
    }

    [Fact]
    public async Task StopSession_CancelsPendingShellConfirmation()
    {
        var shell = new Mock<IAction>();
        shell.Setup(a => a.Name).Returns("run_command");
        shell.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.ConfirmationRequired);
        shell.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        _router.RegisterAction(shell.Object);
        _settings.SetupGet(s => s.Settings).Returns(TestSettings());
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        var calls = 0;
        var provider = new CancellableProvider(_ =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new AIResponse
                {
                    ToolCalls = new List<AIToolCall>
                    {
                        new() { ToolName = "run_command",
                            Arguments = new Dictionary<string, object> { ["command"] = "echo hello" } }
                    }
                }
                : new AIResponse { TextResponse = "Done" });
        });
        var assistant = new AIAssistant(_router, new StubProviderFactory(provider),
            _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);
        var vm = BuildViewModel(assistant);
        _audio.Setup(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CapturedAudio(true, new byte[] { 1, 2 }, TimeSpan.FromSeconds(1), null));

        var session = vm.StartVoiceSessionAsync();
        await WaitUntilAsync(() => vm.HasPendingConfirmation);
        vm.PendingConfirmationText.Should().Contain("echo hello");
        vm.StopSessionCommand.Execute(null);
        await session.WaitAsync(TimeSpan.FromSeconds(2));

        vm.HasPendingConfirmation.Should().BeFalse();
        vm.SessionActive.Should().BeFalse();
        shell.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VoiceSession_RecoversFromCaptureErrorAndContinues()
    {
        var processed = 0;
        _settings.SetupGet(s => s.Settings).Returns(TestSettings());
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var assistant = new AIAssistant(_router,
            new StubProviderFactory(new CapturingProvider(_ => processed++)),
            _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);
        var vm = BuildViewModel(assistant);
        var captures = 0;
        _audio.Setup(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken _) =>
            {
                captures++;
                if (captures == 1)
                    throw new InvalidOperationException("Capture device temporarily unavailable");
                if (captures == 3)
                    vm.StopSessionCommand.Execute(null);
                return Task.FromResult(new CapturedAudio(true, new byte[] { 1, 2 },
                    TimeSpan.FromSeconds(1), null));
            });

        await vm.StartVoiceSessionAsync();

        captures.Should().Be(3);
        processed.Should().Be(1);
        vm.SessionActive.Should().BeFalse();
    }

    [Fact]
    public async Task SecondCommand_IncludesPreviousExchange_ForFollowUpContext()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);

        IReadOnlyList<(string Role, string Content)>? capturedHistory = null;
        var provider = new CapturingProvider(ctx => capturedHistory = ctx);
        var factory = new StubProviderFactory(provider);
        var settings = TestSettings();
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("key");
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var assistant = new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);

        // Simulate the ViewModel: first command plus its assistant reply is kept in history,
        // then the follow-up is processed with that history attached.
        var history = new List<(string Role, string Content)>
        {
            ("user", "Explain dependency injection."),
            ("assistant", "DI is passing dependencies into an object.")
        };

        await assistant.ProcessAsync("Give me a simpler example.", history);

        capturedHistory.Should().NotBeNull();
        capturedHistory!.Should().HaveCount(2);
        capturedHistory![0].Content.Should().Be("Explain dependency injection.");
        capturedHistory![1].Content.Should().Be("DI is passing dependencies into an object.");
    }

    [Fact]
    public async Task Cancel_BusyOperation_DoesNotTerminateAndStaysReady()
    {
        _parser.Setup(p => p.TryParseLocal(It.IsAny<string>())).Returns((Intent?)null);

        // A provider that hangs until the token is cancelled (like a real HTTP call),
        // so the VM is mid-flight when we cancel.
        var pendingCalls = new List<CancellationToken>();
        var cancelProvider = new CancellableProvider(ct =>
        {
            pendingCalls.Add(ct);
            if (pendingCalls.Count > 1)
                return Task.FromResult(new AIResponse { TextResponse = "Ready again." });
            var tcs = new TaskCompletionSource<AIResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            ct.Register(() => { tcs.TrySetException(new OperationCanceledException(ct)); });
            return tcs.Task;
        });
        var factory = new StubProviderFactory(cancelProvider);
        var settings = TestSettings();
        _settings.SetupGet(s => s.Settings).Returns(settings);
        _secrets.Setup(s => s.GetSecretAsync(It.IsAny<string>())).ReturnsAsync("key");
        _secrets.Setup(s => s.SecretExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        var assistant = new AIAssistant(_router, factory, _settings.Object, _secrets.Object, NullLogger<AIAssistant>.Instance);
        var vm = BuildViewModel(assistant);

        vm.CommandText = "explain quantum physics";
        var runTask = vm.ExecuteForTesting();

        // Let the request start, then cancel it
        await WaitUntilAsync(() => pendingCalls.Count > 0);
        vm.CancelCommand.Execute(null);

        await runTask;

        // Session survived: not terminated, back to Idle, ready for another command
        vm.VoiceState.Should().Be(VoiceState.Idle);
        FindResult(vm, "Cancelled").Should().NotBeNull();
        pendingCalls.Should().NotBeEmpty();
        pendingCalls[0].IsCancellationRequested.Should().BeTrue();

        // Now the next command works in the same session
        vm.CommandText = "Open Chrome";
        await vm.ExecuteForTesting();

        FindResult(vm, "Ready again.").Should().NotBeNull();
    }

    [Fact]
    public async Task RepeatedCommands_SameSession_KeepWorking()
    {
        var vm = BuildViewModel(BuildAssistant());

        vm.CommandText = "Open Chrome";
        await vm.ExecuteForTesting();
        FindResult(vm, "Done.").Should().NotBeNull();
        vm.CommandText.Should().BeEmpty();

        // Second command in the SAME session still works
        vm.CommandText = "Open Notepad";
        await vm.ExecuteForTesting();
        vm.Results.Should().HaveCount(1); // results were refreshed, old ones cleared
        FindResult(vm, "Done.").Should().NotBeNull();
        vm.IsBusy.Should().BeFalse();
        vm.CommandText.Should().BeEmpty();
        _parser.Verify(p => p.TryParseLocal(It.IsAny<string>()), Times.Never);
    }

    private static ResultItem? FindResult(MainViewModel vm, string title)
        => vm.Results.FirstOrDefault(r => r.Title == title);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition not met.");
    }

    private class CancellableProvider : IAIProvider
    {
        private readonly Func<CancellationToken, Task<AIResponse>> _handler;
        public CancellableProvider(Func<CancellationToken, Task<AIResponse>> handler) => _handler = handler;
        public string Name => "cancellable";
        public Task<ProviderTestResult> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(new ProviderTestResult { Success = true, Message = "ok" });
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<AIResponse> ProcessQueryAsync(
            string query,
            IEnumerable<AITool> availableTools,
            IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
            CancellationToken cancellationToken = default,
            byte[]? audioWav = null)
            => _handler(cancellationToken);
    }

    private class StubProviderFactory : AIProviderFactory
    {
        private readonly IAIProvider _provider;
        public StubProviderFactory(IAIProvider provider)
            : base(null!, null!, NullLoggerFactory.Instance, new HttpClient())
        {
            _provider = provider;
        }
        public override IAIProvider CreateProvider() => _provider;
    }

    private class CapturingProvider : IAIProvider
    {
        private readonly Action<IReadOnlyList<(string Role, string Content)>?> _onQuery;
        public CapturingProvider(Action<IReadOnlyList<(string Role, string Content)>?> onQuery)
            => _onQuery = onQuery;
        public string Name => "capture";

        public Task<ProviderTestResult> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(new ProviderTestResult { Success = true, Message = "ok" });
        public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<AIResponse> ProcessQueryAsync(
            string query,
            IEnumerable<AITool> availableTools,
            IReadOnlyList<(string Role, string Content)>? conversationHistory = null,
            CancellationToken cancellationToken = default,
            byte[]? audioWav = null)
        {
            _onQuery(conversationHistory);
            return Task.FromResult(new AIResponse { TextResponse = "Sure. Simplest example..." });
        }
    }
}
