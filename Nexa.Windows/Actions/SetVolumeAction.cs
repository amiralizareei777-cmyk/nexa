using Nexa.Windows.Audio;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to set the system volume.
/// </summary>
public class SetVolumeAction : IAction
{
    private readonly AudioController _audio;

    public string Name => "set_volume";
    public string Description => "Sets the system volume to a specific level";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public SetVolumeAction(AudioController audio)
    {
        _audio = audio;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return parameters.ContainsKey("level") &&
               (parameters["level"] is int or string);
    }

    public async Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameters))
        {
            return ActionResult.Failed("Missing required parameter: level (0-100)");
        }

        var level = parameters["level"] switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        };

        if (level < 0 || level > 100)
        {
            return ActionResult.Failed("Volume level must be between 0 and 100");
        }

        var success = await _audio.SetVolumeAsync(level);
        if (success)
        {
            return ActionResult.Successful($"Volume set to {level}%", new Dictionary<string, object> { ["volume"] = level });
        }

        return ActionResult.Failed("Failed to set volume");
    }
}