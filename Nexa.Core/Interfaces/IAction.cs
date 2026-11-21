namespace Nexa.Core.Interfaces;

/// <summary>
/// Defines an executable action that Nexa can perform.
/// </summary>
public interface IAction
{
    /// <summary>
    /// Gets the unique name of this action.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a human-readable description of what this action does.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the risk level of this action.
    /// </summary>
    ActionRiskLevel RiskLevel { get; }

    /// <summary>
    /// Determines whether this action can execute with the given parameters.
    /// </summary>
    bool CanExecute(Dictionary<string, object> parameters);

    /// <summary>
    /// Executes the action asynchronously.
    /// </summary>
    Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
}
