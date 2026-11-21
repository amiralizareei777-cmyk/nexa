using System.Diagnostics;
using Nexa.Core.Interfaces;
using Nexa.Core.Models;

namespace Nexa.Windows.Actions;

/// <summary>
/// Runs an arbitrary shell command via cmd.exe. This action has
/// ConfirmationRequired risk — the user sees the command in a confirmation
/// prompt before it executes, so they can approve or decline.
/// </summary>
public class RunShellCommandAction : IAction
{
    public string Name => "run_command";
    public string Description => "Runs an arbitrary shell command";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.ConfirmationRequired;

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("command") &&
               parameters["command"] is string cmd &&
               !string.IsNullOrWhiteSpace(cmd);
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
            return ActionResult.Failed("Missing required parameter: command");

        var command = parameters["command"]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrEmpty(command))
            return ActionResult.Failed("Command cannot be empty.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/d /s /c \"{command}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            }
        };

        try
        {
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();
            var outputLock = new object();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) lock (outputLock)
                    if (outputBuilder.Length < 5000) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) lock (outputLock)
                    if (errorBuilder.Length < 5000) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch { }
            }))
            {
                var timeout = TimeSpan.FromSeconds(60);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return ActionResult.Failed("Command was cancelled.");

                string stdout, stderr;
                lock (outputLock)
                {
                    stdout = outputBuilder.ToString().Trim();
                    stderr = errorBuilder.ToString().Trim();
                }
                var exitCode = process.ExitCode;

                if (exitCode != 0)
                {
                    var detail = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                    return ActionResult.Failed($"Command failed (exit {exitCode}): {Truncate(detail, 300)}");
                }

                var output = string.IsNullOrEmpty(stdout) ? "(no output)" : Truncate(stdout, 500);
                return ActionResult.Successful($"Command completed (exit {exitCode}). Output: {output}");
            }
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            return ActionResult.Failed("Command was cancelled.");
        }
        catch (TimeoutException)
        {
            KillQuietly(process);
            return ActionResult.Failed("Command timed out after 60 seconds.");
        }
        catch (Exception ex)
        {
            KillQuietly(process);
            return ActionResult.Failed($"Failed to run command: {ex.Message}");
        }
    }

    private static void KillQuietly(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "... (truncated)";
    }
}
