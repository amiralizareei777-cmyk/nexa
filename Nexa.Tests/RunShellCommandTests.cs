using FluentAssertions;
using Nexa.Windows.Actions;
using Xunit;

namespace Nexa.Tests;

public class RunShellCommandTests
{
    [Fact]
    public async Task EchoCommand_ReturnsActualOutput()
    {
        var action = new RunShellCommandAction();
        var result = await action.ExecuteAsync(new Dictionary<string, object>
        {
            ["command"] = "echo nexa-shell-test"
        });

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("nexa-shell-test");
    }

    [Fact]
    public async Task NonzeroExit_IsReportedAsFailureEvenWithoutStderr()
    {
        var action = new RunShellCommandAction();
        var result = await action.ExecuteAsync(new Dictionary<string, object>
        {
            ["command"] = "exit /b 7"
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("exit 7");
    }
}
