using FluentAssertions;
using Xunit;
using Nexa.Core.AI;
using Nexa.Core.Models;

namespace Nexa.Tests;

public class VoiceResponseDeciderTests
{
    [Fact]
    public void GetConfirmationToSpeak_ShortSuccessAction_ReturnsMessage()
    {
        var result = ActionResult.Successful("Opened Google Chrome");
        VoiceResponseDecider.GetConfirmationToSpeak(result, null).Should().Be("Opened Google Chrome");
    }

    [Fact]
    public void GetConfirmationToSpeak_FailedAction_ReturnsNull()
    {
        var result = ActionResult.Failed("Something went wrong");
        VoiceResponseDecider.GetConfirmationToSpeak(result, null).Should().BeNull();
    }

    [Fact]
    public void GetConfirmationToSpeak_LongAiAnswer_IsNotSpoken()
    {
        var longAnswer = string.Join(' ', Enumerable.Repeat("word", 200));
        var result = ActionResult.Successful("Answer from AI");
        VoiceResponseDecider.GetConfirmationToSpeak(result, longAnswer).Should().BeNull();
    }

    [Fact]
    public void GetConfirmationToSpeak_ShortAiAnswer_IsSpoken()
    {
        var result = ActionResult.Successful("Answer from AI");
        VoiceResponseDecider.GetConfirmationToSpeak(result, "Yes, it works.").Should().Be("Yes, it works.");
    }

    [Fact]
    public void GetConfirmationToSpeak_LongActionMessage_IsNotSpoken()
    {
        var longMessage = string.Join(' ', Enumerable.Repeat("detail", 40));
        var result = ActionResult.Successful(longMessage);
        VoiceResponseDecider.GetConfirmationToSpeak(result, null).Should().BeNull();
    }

    [Fact]
    public void GetConfirmationToSpeak_EmptyMessage_ReturnsNull()
    {
        var result = ActionResult.Successful("   ");
        VoiceResponseDecider.GetConfirmationToSpeak(result, null).Should().BeNull();
    }
}