using FluentAssertions;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using Nexa.Windows.Speech;
using Xunit;

namespace Nexa.Tests;

public class WakeWordTests
{
    [Fact(Skip = "Requires an installed Windows speech recognizer and audio device; run manually on the target PC.")]
    public void LocalRecognizer_RecognizesSynthesizedNexaWakeWord()
    {
        var recognizer = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(r => r.Culture.Name == "en-US")
            ?? throw new InvalidOperationException("This Windows installation has no en-US recognizer.");

        using var wave = new MemoryStream();
        using (var synth = new SpeechSynthesizer())
        {
            synth.SetOutputToWaveStream(wave);
            synth.Speak("Nexa");
        }
        wave.Position = 0;

        using var engine = new SpeechRecognitionEngine(recognizer);
        var builder = new GrammarBuilder("Nexa") { Culture = recognizer.Culture };
        engine.LoadGrammar(new Grammar(builder));
        engine.SetInputToWaveStream(wave);
        var result = engine.Recognize(TimeSpan.FromSeconds(5));

        result.Should().NotBeNull();
        result!.Text.Should().Be("Nexa");
        result.Confidence.Should().BeGreaterOrEqualTo(0.45f);
    }

    [Fact(Skip = "Requires exclusive access to a physical Windows microphone; run manually on the target PC.")]
    public async Task WakeWordListener_StartsAndReleasesMicrophone()
    {
        using var listener = new WakeWordListener();
        listener.TryStart().Should().BeTrue();
        await listener.PauseAsync();
        listener.TryStart().Should().BeTrue();
        await listener.PauseAsync();
    }
}
