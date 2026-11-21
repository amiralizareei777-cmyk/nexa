using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Nexa.Windows.AppLauncher;

/// <summary>
/// Discovers and launches installed Windows applications.
/// </summary>
public class ApplicationLauncher
{
    private readonly List<InstalledApp> _cachedApps = new();
    private DateTime _lastIndexTime = DateTime.MinValue;
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(10);

    public async Task<List<InstalledApp>> GetInstalledAppsAsync()
    {
        if (DateTime.Now - _lastIndexTime < _cacheLifetime && _cachedApps.Count > 0)
        {
            return _cachedApps;
        }

        await Task.Run(() =>
        {
            _cachedApps.Clear();

            // Index Start Menu
            IndexStartMenu();

            // Index common application paths
            IndexCommonPaths();

            // Index Desktop shortcuts
            IndexDesktop();

            _lastIndexTime = DateTime.Now;
        });

        return _cachedApps;
    }

    private void IndexStartMenu()
    {
        var startMenuPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };

        foreach (var basePath in startMenuPaths)
        {
            if (Directory.Exists(basePath))
            {
                IndexShortcutsInDirectory(basePath);
            }
        }
    }

    private void IndexDesktop()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (Directory.Exists(desktop))
        {
            IndexShortcutsInDirectory(desktop);
        }
    }

    private void IndexCommonPaths()
    {
        // Common installation directories
        var commonPaths = new[]
        {
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        };

        foreach (var basePath in commonPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            try
            {
                var exeFiles = Directory.EnumerateFiles(basePath, "*.exe", SearchOption.AllDirectories)
                    .Take(200); // Limit for performance

                foreach (var exePath in exeFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(exePath);
                        var name = Path.GetFileNameWithoutExtension(exePath);

                        if (!_cachedApps.Any(a => a.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            _cachedApps.Add(new InstalledApp
                            {
                                Name = name,
                                ExecutablePath = exePath,
                                Source = "ProgramFiles"
                            });
                        }
                    }
                    catch { /* Skip inaccessible files */ }
                }
            }
            catch { /* Skip inaccessible directories */ }
        }
    }

    private void IndexShortcutsInDirectory(string directory)
    {
        try
        {
            var shortcuts = Directory.EnumerateFiles(directory, "*.lnk", SearchOption.AllDirectories);

            foreach (var shortcut in shortcuts)
            {
                try
                {
                    var targetPath = ResolveShortcut(shortcut);
                    if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                    {
                        var name = Path.GetFileNameWithoutExtension(shortcut);

                        if (!_cachedApps.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            _cachedApps.Add(new InstalledApp
                            {
                                Name = name,
                                ExecutablePath = targetPath,
                                Source = "Shortcut"
                            });
                        }
                    }
                }
                catch { /* Skip invalid shortcuts */ }
            }
        }
        catch { /* Skip inaccessible directories */ }
    }

    private string? ResolveShortcut(string shortcutPath)
    {
        try
        {
            var wshType = Type.GetTypeFromProgID("WScript.Shell");
            if (wshType == null) return null;

            dynamic shell = Activator.CreateInstance(wshType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            return (string)shortcut.TargetPath;
        }
        catch
        {
            return null;
        }
    }

    public async Task<InstalledApp?> FindAppAsync(string query)
    {
        var apps = await GetInstalledAppsAsync();
        return AppMatcher.FindBestMatch(apps, query);
    }

    public Task<bool> LaunchAppAsync(string executablePath, string? arguments = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments ?? "",
                UseShellExecute = true
            };

            Process.Start(startInfo);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}

public class InstalledApp
{
    public required string Name { get; set; }
    public required string ExecutablePath { get; set; }
    public required string Source { get; set; }
}

