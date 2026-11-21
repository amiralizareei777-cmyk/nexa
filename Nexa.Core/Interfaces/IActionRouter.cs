namespace Nexa.Core.Interfaces;

/// <summary>
/// Routes user commands to appropriate actions.
/// </summary>
public interface IActionRouter
{
    /// <summary>
    /// Registers an action with the router.
    /// </summary>
    void RegisterAction(IAction action);

    /// <summary>
    /// Routes a command to the appropriate action(s) and executes them.
    /// </summary>
    Task<ActionResult> RouteAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered actions.
    /// </summary>
    IReadOnlyCollection<IAction> GetAllActions();
}
