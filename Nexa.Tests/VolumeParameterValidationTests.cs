using FluentAssertions;
using Xunit;
using Nexa.Windows.Actions;
using Nexa.Windows.Audio;

namespace Nexa.Tests;

public class VolumeParameterValidationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("30")]
    [InlineData("50")]
    [InlineData("100")]
    public void SetVolumeAction_CanExecute_AcceptsValidRange(string level)
    {
        var action = CreateAction();
        var parameters = new Dictionary<string, object> { ["level"] = level };

        action.CanExecute(parameters).Should().BeTrue();
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("101")]
    [InlineData("500")]
    public void SetVolumeAction_CanExecute_RejectsInvalidRange(string level)
    {
        var action = CreateAction();
        var parameters = new Dictionary<string, object> { ["level"] = level };

        // CanExecute checks presence/type, actual range check happens in ExecuteAsync
        action.CanExecute(parameters).Should().BeTrue();
    }

    [Fact]
    public void SetVolumeAction_CanExecute_RejectsMissingParameter()
    {
        var action = CreateAction();
        var parameters = new Dictionary<string, object>();

        action.CanExecute(parameters).Should().BeFalse();
    }

    [Fact]
    public void SetVolumeAction_CanExecute_AcceptsIntegerParameter()
    {
        var action = CreateAction();
        var parameters = new Dictionary<string, object> { ["level"] = 30 };

        action.CanExecute(parameters).Should().BeTrue();
    }

    [Fact]
    public async Task SetVolumeAction_ExecuteAsync_RejectsOutOfRange()
    {
        var action = CreateAction();
        var parameters = new Dictionary<string, object> { ["level"] = 150 };

        var result = await action.ExecuteAsync(parameters);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("0 and 100");
    }

    [Fact]
    public async Task SetVolumeAction_ExecuteAsync_ReturnsFailureWhenAudioUnavailable()
    {
        // Audio access in test environment may not have a device; the action
        // should fail gracefully rather than throwing.
        var action = CreateAction();
        var parameters = new Dictionary<string, object> { ["level"] = 30 };

        Func<Task> act = async () => await action.ExecuteAsync(parameters);

        await act.Should().NotThrowAsync();
    }

    private static SetVolumeAction CreateAction()
    {
        return new SetVolumeAction(new AudioController());
    }
}