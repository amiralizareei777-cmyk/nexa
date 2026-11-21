namespace Nexa.Core.Models;

/// <summary>
/// Risk level for an action.
/// </summary>
public enum ActionRiskLevel
{
    /// <summary>
    /// Safe to execute without confirmation.
    /// </summary>
    Safe,

    /// <summary>
    /// Requires user confirmation before execution.
    /// </summary>
    ConfirmationRequired,

    /// <summary>
    /// Blocked from execution.
    /// </summary>
    Blocked
}
