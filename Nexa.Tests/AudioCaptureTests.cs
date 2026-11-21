using System.Text;
using FluentAssertions;
using Nexa.Windows.Speech;
using Xunit;

namespace Nexa.Tests;

public class AudioCaptureTests
{
    [Fact]
    public void WavBuilder_ProducesValidPcmHeaderAndPreservesSamples()
    {
        var pcm = new byte[] { 0, 1, 2, 3 };

        var wav = WavBuilder.ToWav(pcm);

        wav.Length.Should().Be(48);
        Encoding.ASCII.GetString(wav, 0, 4).Should().Be("RIFF");
        BitConverter.ToInt32(wav, 4).Should().Be(40);
        Encoding.ASCII.GetString(wav, 8, 4).Should().Be("WAVE");
        BitConverter.ToInt16(wav, 22).Should().Be(1);
        BitConverter.ToInt32(wav, 24).Should().Be(16000);
        BitConverter.ToInt16(wav, 34).Should().Be(16);
        Encoding.ASCII.GetString(wav, 36, 4).Should().Be("data");
        BitConverter.ToInt32(wav, 40).Should().Be(pcm.Length);
        wav[44..].Should().Equal(pcm);
    }
}
