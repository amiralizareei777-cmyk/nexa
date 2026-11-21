using System.Diagnostics;
using System.IO;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to open Visual Studio Code.
/// </summary>
public class VsCodeAction : IAction
{
    public string Name => "open_vscode";
    public string Description => "Opens Visual Studio Code, optionally in a specific folder";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return true; // No required parameters
    }

    public Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var folderPath = parameters.GetValueOrDefault("folder_path")?.ToString();

        // Find VS Code executable
        var vsCodePath = FindVsCodePath();
        if (string.IsNullOrEmpty(vsCodePath))
        {
            return Task.FromResult(ActionResult.Failed("Visual Studio Code not found. Please ensure it's installed."));
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = vsCodePath,
                Arguments = folderPath ?? "",
                UseShellExecute = true
            };

            Process.Start(startInfo);

            var message = string.IsNullOrEmpty(folderPath)
                ? "Opened Visual Studio Code"
                : $"Opened Visual Studio Code in {folderPath}";

            return Task.FromResult(ActionResult.Successful(message));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.Failed($"Failed to open VS Code: {ex.Message}"));
        }
    }

    private string? FindVsCodePath()
    {
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
            "code.exe" // If in PATH
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Try to find via PATH
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "code",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadLine();
            process.WaitForExit(1000);

            if (!string.IsNullOrEmpty(output) && File.Exists(output))
            {
                return "code";
            }
        }
        catch { }

        return null;
    }
}
