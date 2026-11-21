using Nexa.Windows.Shell;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to open a folder.
/// </summary>
public class OpenFolderAction : IAction
{
    private readonly ShellIntegration _shell;

    public string Name => "open_folder";
    public string Description => "Opens a folder in Windows Explorer";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public OpenFolderAction(ShellIntegration shell)
    {
        _shell = shell;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("path") && parameters["path"] is string;
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
        {
            return ActionResult.Failed("Missing required parameter: path");
        }

        var path = parameters["path"]?.ToString() ?? "";

        // Handle special folder names
        var resolvedPath = _shell.GetSpecialFolderPath(path);
        if (string.IsNullOrEmpty(resolvedPath))
        {
            resolvedPath = path;
        }

        var success = await _shell.OpenFolderAsync(resolvedPath);
        if (success)
        {
            return ActionResult.Successful($"Opened {path}");
        }

        return ActionResult.Failed($"Could not open folder: {path}");
    }
}