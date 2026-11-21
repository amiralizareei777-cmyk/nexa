using Nexa.Windows.SystemInfo;

namespace Nexa.Windows.Actions;

/// <summary>
/// Action to get system information.
/// </summary>
public class SystemInfoAction : IAction
{
    private readonly SystemInfoService _systemInfo;

    public string Name => "get_system_info";
    public string Description => "Gets system information like CPU, RAM, Windows version, and disk space";
    public ActionRiskLevel RiskLevel => ActionRiskLevel.Safe;

    public SystemInfoAction(SystemInfoService systemInfo)
    {
        _systemInfo = systemInfo;
    }

    public bool CanExecute(Dictionary<string, object> parameters)
    {
        return true; // No required parameters
    }

    public Task<ActionResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        var infoType = parameters.GetValueOrDefault("info_type", "all")?.ToString() ?? "all";

        string result = infoType.ToLowerInvariant() switch
        {
            "cpu" => $"CPU: {_systemInfo.GetCpuInfo()}",
            "ram" or "memory" => $"RAM: {_systemInfo.GetRamInfo()}",
            "windows" or "os" or "version" => $"Windows: {_systemInfo.GetWindowsVersion()}",
            "disk" or "storage" or "drive" => $"Disk Space: {_systemInfo.GetDiskSpaceInfo()}",
            _ => FormatAllInfo()
        };

        return Task.FromResult(ActionResult.Successful(result));
    }

    private string FormatAllInfo()
    {
        var info = _systemInfo.GetAllSystemInfo();
        return string.Join("\n", info.Select(kv => $"{kv.Key}: {kv.Value}"));
    }
}