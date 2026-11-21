using Microsoft.Extensions.Logging;

namespace Nexa.Core.Parsing;

/// <summary>
/// Routes commands to appropriate actions.
/// </summary>
public class ActionRouter : IActionRouter
{
    private readonly Dictionary<string, IAction> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIntentParser _intentParser;
    private readonly ILogger<ActionRouter>? _logger;

    public ActionRouter(IIntentParser intentParser, ILogger<ActionRouter>? logger = null)
    {
        _intentParser = intentParser;
        _logger = logger;
    }

    public void RegisterAction(IAction action)
    {
        _actions[action.Name] = action;
        _logger?.LogInformation("Registered action: {ActionName}", action.Name);
    }

    public IReadOnlyCollection<IAction> GetAllActions()
    {
        return _actions.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Attempts to parse a command locally without invoking any AI provider.
    /// </summary>
    public Intent? ParseLocal(string command)
    {
        return _intentParser.TryParseLocal(command);
    }

    public async Task<ActionResult> RouteAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ActionResult.Failed("Empty command");
        }

        // Try local parsing first
        var intent = _intentParser.TryParseLocal(command);

        if (intent == null)
        {
            _logger?.LogDebug("No local intent found for: {Command}", command);
            return ActionResult.Failed("Could not understand the command. Try being more specific or configure an AI provider.");
        }

        _logger?.LogInformation("Routing to action: {ActionName}", intent.ActionName);

        return await ExecuteIntentAsync(intent, cancellationToken);
    }

    public Task<ActionResult> ExecuteIntentAsync(Intent intent, CancellationToken cancellationToken = default)
        => ExecuteIntentCoreAsync(intent, cancellationToken, confirmationGranted: false);

    internal Task<ActionResult> ExecuteConfirmedIntentAsync(Intent intent, CancellationToken cancellationToken = default)
        => ExecuteIntentCoreAsync(intent, cancellationToken, confirmationGranted: true);

    private async Task<ActionResult> ExecuteIntentCoreAsync(
        Intent intent, CancellationToken cancellationToken, bool confirmationGranted)
    {
        if (!_actions.TryGetValue(intent.ActionName, out var action))
        {
            _logger?.LogWarning("Action not found: {ActionName}", intent.ActionName);
            return ActionResult.Failed($"Unknown action: {intent.ActionName}");
        }

        if (!action.CanExecute(intent.Parameters))
        {
            _logger?.LogWarning("Invalid parameters for action: {ActionName}", intent.ActionName);
            return ActionResult.Failed($"Invalid parameters for action: {intent.ActionName}");
        }

        // Check risk level
        if (action.RiskLevel == ActionRiskLevel.Blocked)
        {
            return ActionResult.Failed($"Action {intent.ActionName} is blocked");
        }

        if (action.RiskLevel == ActionRiskLevel.ConfirmationRequired && !confirmationGranted)
        {
            return ActionResult.Failed($"Action {intent.ActionName} requires confirmation");
        }

        // Execute the action
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action.ExecuteAsync(intent.Parameters, cancellationToken);
            stopwatch.Stop();

            _logger?.LogInformation(
                "Action {ActionName} completed in {Duration}ms. Success: {Success}",
                intent.ActionName,
                stopwatch.ElapsedMilliseconds,
                result.Success
            );

            return result;
        }
        catch (OperationCanceledException)
        {
            return ActionResult.Failed("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Action {ActionName} failed", intent.ActionName);
            return ActionResult.Failed($"Action failed: {ex.Message}", ex);
        }
    }

    public IAction? GetAction(string name)
    {
        return _actions.GetValueOrDefault(name);
    }
}
