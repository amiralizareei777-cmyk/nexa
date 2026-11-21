using Nexa.Windows.Shell;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to open a URL in the default browser.
/// </summary>
public class OpenUrlAction : IAction
{
    private readonly ShellIntegration _shell;

    public string Name => "open_url";
    public string Description => "Opens a URL in the default browser";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public OpenUrlAction(ShellIntegration shell)
    {
        _shell = shell;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("url") && parameters["url"] is string;
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
        {
            return ActionResult.Failed("Missing required parameter: url");
        }

        var url = parameters["url"]?.ToString() ?? "";

        // Add protocol if missing
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        var success = await _shell.OpenUrlAsync(url);
        if (success)
        {
            return ActionResult.Successful($"Opened {url}");
        }

        return ActionResult.Failed($"Could not open URL: {url}");
    }
}