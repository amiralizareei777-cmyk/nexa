using System.Diagnostics;
using System.IO;
using Nexa.Windows.Shell;

namespace Nexa.Windows.Actions;

/// <summary>Opens a saved file in its normal app, Notepad, or a web browser.</summary>
public sealed class OpenFileAction : IAction
{
    private readonly ShellIntegration _shell;
    private readonly RecentArtifactStore _recent;

    public OpenFileAction(ShellIntegration shell, RecentArtifactStore recent)
    {
        _shell = shell;
        _recent = recent;
    }

    public string Name => "open_file";
    public string Description => "Opens a saved file, including the website file Nexa most recently created";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public bool CanExecute(Dictionary<string, object> parameters)
        => !parameters.TryGetValue("path", out var p) || p is string;

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters,
        CancellationToken cancellationToken = default)
    {
        var pathValue = parameters.TryGetValue("path", out var raw) ? raw?.ToString()?.Trim() : null;
        var path = string.IsNullOrWhiteSpace(pathValue) || pathValue.Equals("last", StringComparison.OrdinalIgnoreCase)
            ? _recent.LastCreatedPath
            : Path.IsPathFullyQualified(pathValue)
                ? Path.GetFullPath(pathValue)
                : WriteFileAction.ResolvePath(pathValue);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return ActionResult.Failed("The saved file was not found. Give its path or create it first.");

        var mode = parameters.TryGetValue("mode", out var m) ? m?.ToString()?.ToLowerInvariant() : "default";
        if (mode is not ("default" or "notepad" or "browser"))
            return ActionResult.Failed("Choose default, notepad, or browser.");
        if (mode == "browser" && !new[] { ".html", ".htm" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            return ActionResult.Failed("Browser mode is for HTML files.");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool opened;
            if (mode == "notepad")
            {
                var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
                start.ArgumentList.Add(path);
                opened = Process.Start(start) != null;
            }
            else if (mode == "browser")
            {
                opened = await _shell.OpenUrlAsync(new Uri(path).AbsoluteUri);
            }
            else
            {
                opened = await _shell.OpenFileAsync(path);
            }
            return opened
                ? ActionResult.Successful($"Opened {path}", new Dictionary<string, object> { ["path"] = path })
                : ActionResult.Failed($"Could not open {path}");
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return ActionResult.Failed($"Could not open the file: {ex.Message}", ex);
        }
    }
}
