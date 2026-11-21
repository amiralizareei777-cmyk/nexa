using FluentAssertions;
using Moq;
using Xunit;
using Nexa.Core.Parsing;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Tests;

public class ActionRouterTests
{
    private readonly Mock<IIntentParser> _parser;
    private readonly ActionRouter _router;

    public ActionRouterTests()
    {
        _parser = new Mock<IIntentParser>();
        _router = new ActionRouter(_parser.Object);
    }

    [Fact]
    public void RegisterAction_AddsActionToRegistry()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("test_action");

        _router.RegisterAction(action.Object);

        _router.GetAllActions().Should().Contain(action.Object);
    }

    [Fact]
    public async Task RouteAsync_ExecutesActionWhenIntentParsed()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("open_application");
        action.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.Safe);
        action.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        action.Setup(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActionResult.Successful("Opened Chrome"));

        _router.RegisterAction(action.Object);

        var intent = new Intent { ActionName = "open_application", Parameters = new Dictionary<string, object> { ["app_name"] = "Chrome" } };
        _parser.Setup(p => p.TryParseLocal("Open Chrome")).Returns(intent);

        var result = await _router.RouteAsync("Open Chrome");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Opened Chrome");
    }

    [Fact]
    public async Task RouteAsync_ReturnsFailureWhenIntentNotFound()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("some_action");
        _router.RegisterAction(action.Object);

        _parser.Setup(p => p.TryParseLocal("gibberish command")).Returns((Intent?)null);

        var result = await _router.RouteAsync("gibberish command");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("understand");
    }

    [Fact]
    public async Task RouteAsync_ReturnsFailureWhenActionNotRegistered()
    {
        var intent = new Intent { ActionName = "nonexistent_action" };
        _parser.Setup(p => p.TryParseLocal("do something")).Returns(intent);

        var result = await _router.RouteAsync("do something");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("nonexistent_action");
    }

    [Fact]
    public async Task RouteAsync_BlocksActionsWithBlockedRiskLevel()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("dangerous");
        action.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.Blocked);
        action.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);

        _router.RegisterAction(action.Object);

        var intent = new Intent { ActionName = "dangerous" };
        _parser.Setup(p => p.TryParseLocal("dangerous")).Returns(intent);

        var result = await _router.RouteAsync("dangerous");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteIntentAsync_RequiresConfirmationForGuardedAction()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("run_command");
        action.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.ConfirmationRequired);
        action.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        _router.RegisterAction(action.Object);

        var result = await _router.ExecuteIntentAsync(new Intent
        {
            ActionName = "run_command",
            Parameters = new Dictionary<string, object> { ["command"] = "echo hello" }
        });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("requires confirmation");
        action.Verify(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RouteAsync_RejectsInvalidParameters()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("set_volume");
        action.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.Safe);
        action.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(false);

        _router.RegisterAction(action.Object);

        var intent = new Intent { ActionName = "set_volume", Parameters = new Dictionary<string, object>() };
        _parser.Setup(p => p.TryParseLocal("volume")).Returns(intent);

        var result = await _router.RouteAsync("volume");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("parameters");
    }

    [Fact]
    public async Task RouteAsync_HandlesActionExceptions()
    {
        var action = new Mock<IAction>();
        action.Setup(a => a.Name).Returns("failing");
        action.Setup(a => a.RiskLevel).Returns(ActionRiskLevel.Safe);
        action.Setup(a => a.CanExecute(It.IsAny<Dictionary<string, object>>())).Returns(true);
        action.Setup(a => a.ExecuteAsync(It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        _router.RegisterAction(action.Object);

        var intent = new Intent { ActionName = "failing" };
        _parser.Setup(p => p.TryParseLocal("fail")).Returns(intent);

        var result = await _router.RouteAsync("fail");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("boom");
    }

    [Fact]
    public void GetAllActions_ReturnsAllRegisteredActions()
    {
        var action1 = new Mock<IAction>();
        action1.Setup(a => a.Name).Returns("a1");
        var action2 = new Mock<IAction>();
        action2.Setup(a => a.Name).Returns("a2");

        _router.RegisterAction(action1.Object);
        _router.RegisterAction(action2.Object);

        _router.GetAllActions().Count.Should().Be(2);
    }
}
