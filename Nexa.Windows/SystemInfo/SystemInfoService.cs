using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace Nexa.Windows.SystemInfo;

/// <summary>
/// Provides Windows system information.
/// </summary>
public class SystemInfoService
{
    public string GetCpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    // Simplify the name
                    if (name.Contains("Intel"))
                    {
                        return $"Intel Processor ({obj["NumberOfCores"]} cores)";
                    }
                    if (name.Contains("AMD"))
                    {
                        return $"AMD Processor ({obj["NumberOfCores"]} cores)";
                    }
                    return name;
                }
            }
        }
        catch { }

        return "Unknown CPU";
    }

    public string GetRamInfo()
    {
        try
        {
            var info = new Microsoft.VisualBasic.Devices.ComputerInfo();
            var totalGB = info.TotalPhysicalMemory / (1024.0 * 1024 * 1024);
            var availableGB = info.AvailablePhysicalMemory / (1024.0 * 1024 * 1024);
            return $"{(int)totalGB} GB total, {(int)availableGB} GB available";
        }
        catch
        {
            // Fallback using Performance Counter
            try
            {
                var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                var availableMB = ramCounter.NextValue();
                return $"{(int)(availableMB / 1024)} GB available";
            }
            catch
            {
                return "Unknown RAM";
            }
        }
    }

    public string GetWindowsVersion()
    {
        try
        {
            var version = Environment.OSVersion.Version;
            var build = Environment.OSVersion.Version.Build;

            var versionName = version.Major switch
            {
                10 when build >= 22000 => "Windows 11",
                10 => "Windows 10",
                6 => version.Minor switch
                {
                    3 => "Windows 8.1",
                    2 => "Windows 8",
                    1 => "Windows 7",
                    _ => "Windows"
                },
                _ => "Windows"
            };

            return $"{versionName} (Build {build})";
        }
        catch
        {
            return "Unknown Windows version";
        }
    }

    public string GetDiskSpaceInfo()
    {
        try
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .ToList();

            var results = new List<string>();
            foreach (var drive in drives)
            {
                var freeGB = drive.TotalFreeSpace / (1024.0 * 1024 * 1024);
                var totalGB = drive.TotalSize / (1024.0 * 1024 * 1024);
                results.Add($"{drive.Name}: {(int)freeGB} GB free of {(int)totalGB} GB");
            }

            return string.Join(", ", results);
        }
        catch
        {
            return "Unknown disk space";
        }
    }

    public Dictionary<string, string> GetAllSystemInfo()
    {
        return new Dictionary<string, string>
        {
            ["CPU"] = GetCpuInfo(),
            ["RAM"] = GetRamInfo(),
            ["Windows"] = GetWindowsVersion(),
            ["Disk Space"] = GetDiskSpaceInfo()
        };
    }
}