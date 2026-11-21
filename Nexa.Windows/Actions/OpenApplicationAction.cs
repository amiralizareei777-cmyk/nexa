using Nexa.Windows.AppLauncher;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to open an installed application.
/// </summary>
public class OpenApplicationAction : IAction
{
    private readonly ApplicationLauncher _launcher;

    public string Name => "open_application";
    public string Description => "Opens an installed application";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public OpenApplicationAction(ApplicationLauncher launcher)
    {
        _launcher = launcher;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("app_name") && parameters["app_name"] is string;
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
        {
            return ActionResult.Failed("Missing required parameter: app_name");
        }

        var appName = parameters["app_name"]?.ToString() ?? "";
        var app = await _launcher.FindAppAsync(appName);

        if (app == null)
        {
            return ActionResult.Failed($"Could not find application: {appName}");
        }

        var success = await _launcher.LaunchAppAsync(app.ExecutablePath);
        if (success)
        {
            return ActionResult.Successful($"Opened {app.Name}");
        }

        return ActionResult.Failed($"Failed to open {appName}");
    }
}