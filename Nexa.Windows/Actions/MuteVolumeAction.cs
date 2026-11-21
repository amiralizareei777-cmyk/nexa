using Nexa.Windows.Audio;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to mute or unmute the system volume.
/// </summary>
public class MuteVolumeAction : IAction
{
    private readonly AudioController _audio;

    public string Name => "mute_volume";
    public string Description => "Mutes or unmutes the system volume";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public MuteVolumeAction(AudioController audio)
    {
        _audio = audio;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("mute") &&
               (parameters["mute"] is bool or string);
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
        {
            return ActionResult.Failed("Missing required parameter: mute (true/false)");
        }

        var mute = parameters["mute"] switch
        {
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       s.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                       s.Equals("mute", StringComparison.OrdinalIgnoreCase),
            _ => true
        };

        var success = await _audio.MuteAsync(mute);
        if (success)
        {
            var action = mute ? "Muted" : "Unmuted";
            return ActionResult.Successful(action);
        }

        return ActionResult.Failed($"Failed to {(mute ? "mute" : "unmute")} volume");
    }
}