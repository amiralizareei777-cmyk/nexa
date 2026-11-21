using System.IO;
using Nexa.Windows.Screenshot;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to take a screenshot.
/// </summary>
public class ScreenshotAction : IAction
{
    private readonly ScreenshotService _screenshot;

    public string Name => "take_screenshot";
    public string Description => "Takes a screenshot of the full screen or active window";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public ScreenshotAction(ScreenshotService screenshot)
    {
        _screenshot = screenshot;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return true; // Optional parameters only
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        string? path;
        var captureType = parameters.GetValueOrDefault("capture_type", "full_screen")?.ToString() ?? "full_screen";

        if (captureType == "active_window")
        {
            path = await _screenshot.CaptureActiveWindowAsync();
        }
        else
        {
            path = await _screenshot.CaptureFullScreenAsync();
        }

        if (string.IsNullOrEmpty(path))
        {
            return ActionResult.Failed("Failed to capture screenshot");
        }

        return ActionResult.Successful($"Screenshot saved to {Path.GetFileName(path)}",
            new Dictionary<string, object> { ["path"] = path });
    }
}