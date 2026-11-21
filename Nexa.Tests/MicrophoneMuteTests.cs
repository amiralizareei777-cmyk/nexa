using FluentAssertions;
using Moq;
using Nexa.App.ViewModels;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;
using Xunit;

namespace Nexa.Tests;

public class MicrophoneMuteTests
{
    [Fact]
    public async Task MutedMicrophone_DoesNotStartVoiceCapture()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new AppSettings
        {
            Voice = new VoiceSettings { MicrophoneMuted = true }
        });
        var audio = new Mock<IAudioInputService>();
        var vm = new MainViewModel(null!, null!, audio.Object, Mock.Of<ITextToSpeechService>(), settings.Object);

        await vm.StartVoiceSessionAsync();

        vm.SessionActive.Should().BeFalse();
        vm.StatusMessage.Should().Contain("muted");
        audio.Verify(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MutingDuringVoiceCapture_CancelsSession()
    {
        var appSettings = new AppSettings();
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(appSettings);
        var audio = new Mock<IAudioInputService>();
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        audio.Setup(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(async (int _, CancellationToken ct) =>
            {
                captureStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new CapturedAudio(false, null, TimeSpan.Zero, "Cancelled");
            });
        var vm = new MainViewModel(null!, null!, audio.Object, Mock.Of<ITextToSpeechService>(), settings.Object);

        var session = vm.StartVoiceSessionAsync();
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.SessionActive.Should().BeTrue();

        appSettings.Voice.MicrophoneMuted = true;
        vm.NotifyMicrophoneMuteChanged();
        await session.WaitAsync(TimeSpan.FromSeconds(2));

        vm.SessionActive.Should().BeFalse();
        audio.Verify(a => a.CaptureAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
