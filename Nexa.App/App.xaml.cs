using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nexa.App.Windows;
using Nexa.Windows.Actions;
using Nexa.Core.AI;
using Nexa.Core.Parsing;
using Nexa.Core.Interfaces;
using Nexa.Infrastructure.Security;
using Nexa.Infrastructure.Settings;
using Nexa.Windows.AppLauncher;
using Nexa.Windows.Audio;
using Nexa.Windows.FileSearch;
using Nexa.Windows.Screenshot;
using Nexa.Windows.Shell;
using Nexa.Windows.SystemInfo;
using Nexa.Windows.Speech;
using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using SystemDrawing = System.Drawing;

namespace Nexa.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Forms.ToolStripMenuItem? _microphoneMenuItem;
    private SystemDrawing.Icon? _appIcon;
    private CommandPaletteWindow? _commandPalette;
    private SettingsWindow? _settingsWindow;
    private GlobalHotkey? _hotkey;
    private WakeWordListener? _wakeListener;
    private WakeGlowWindow? _wakeGlow;
    private ViewModels.MainViewModel? _mainViewModel;
    private bool _wakeHandling;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstance;
    private readonly SemaphoreSlim _microphoneMuteGate = new(1, 1);
    public bool IsShuttingDown { get; private set; }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr handle);

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not initialized");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\Nexa.SingleInstance", out _ownsSingleInstance);
        if (!_ownsSingleInstance)
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        // Build the host and configure DI
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                ConfigureServices(services);
            })
            .Build();

        // Load settings
        var settingsService = Services.GetRequiredService<ISettingsService>();
        await settingsService.LoadAsync();
        ApplyTheme(settingsService.Settings.General.Theme);
        var keyMigrationSucceeded = await MigrateLegacyApiKeyAsync(settingsService);

        // Initialize system tray
        InitializeTrayIcon();

        // Register global hotkey
        InitializeHotkey();

        // Register actions
        RegisterActions();

        // The main window is the application, not a tray-only command palette.
        ShowCommandPalette();

        if (settingsService.Settings.General.LaunchAtStartup)
            ApplyLaunchAtStartup(true);
        _mainViewModel = Services.GetRequiredService<ViewModels.MainViewModel>();
        if (!keyMigrationSucceeded)
        {
            _mainViewModel.StatusMessage = "The saved API key could not be migrated. Re-enter it in Settings → AI.";
            _mainViewModel.ShowStatus = true;
        }
        _mainViewModel.BeforeVoiceSession += PauseWakeListenerAsync;
        _mainViewModel.VoiceSessionEnded += OnVoiceSessionEnded;
        _wakeListener = Services.GetRequiredService<WakeWordListener>();
        _wakeListener.WakeWordDetected += OnWakeWordDetected;
        await RefreshWakeWordListenerAsync();

        if (settingsService.Settings.General.IsFirstRun)
        {
            ShowSettings(openGuide: true);
            settingsService.Settings.General.IsFirstRun = false;
            await settingsService.SaveAsync();
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ISecretStore, WindowsSecretStore>();

        // Windows integration
        services.AddSingleton<ApplicationLauncher>();
        services.AddSingleton<AudioController>();
        services.AddSingleton<ShellIntegration>();
        services.AddSingleton<FileSearchService>();
        services.AddSingleton<ScreenshotService>();
        services.AddSingleton<SystemInfoService>();
        services.AddSingleton<RecentArtifactStore>();

        // Core services
        services.AddSingleton<IIntentParser, IntentParser>();
        services.AddSingleton<ActionRouter>();

        // AI services
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(60) });
        services.AddSingleton<AIProviderFactory>();
        services.AddSingleton<AIAssistant>();

        // Voice services
        services.AddSingleton<IAudioInputService, Nexa.Windows.Speech.AudioCaptureService>();
        services.AddSingleton<TextToSpeechService>();
        services.AddSingleton<ITextToSpeechService>(sp => sp.GetRequiredService<TextToSpeechService>());
        services.AddSingleton<WakeWordListener>();

        // Actions
        services.AddSingleton<OpenApplicationAction>();
        services.AddSingleton<OpenFolderAction>();
        services.AddSingleton<SetVolumeAction>();
        services.AddSingleton<MuteVolumeAction>();
        services.AddSingleton<SearchFilesAction>();
        services.AddSingleton<ScreenshotAction>();
        services.AddSingleton<VsCodeAction>();
        services.AddSingleton<OpenUrlAction>();
        services.AddSingleton<SystemInfoAction>();
        services.AddSingleton<RunShellCommandAction>();
        services.AddSingleton<WriteFileAction>();
        services.AddSingleton<OpenFileAction>();

        // Register all actions under their interface too, so the router can discover them
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<OpenApplicationAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<OpenFolderAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<SetVolumeAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<MuteVolumeAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<SearchFilesAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<ScreenshotAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<VsCodeAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<OpenUrlAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<SystemInfoAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<RunShellCommandAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<WriteFileAction>());
        services.AddSingleton<IAction>(sp => sp.GetRequiredService<OpenFileAction>());

        // ViewModels
        services.AddSingleton<ViewModels.MainViewModel>();

        // Views
        services.AddTransient<Windows.CommandPaletteWindow>();
        services.AddTransient<Windows.SettingsWindow>();
    }

    private async Task<bool> MigrateLegacyApiKeyAsync(ISettingsService settingsService)
    {
        try
        {
            var secrets = Services.GetRequiredService<ISecretStore>();
            if (!await secrets.SecretExistsAsync("ApiKey")) return true;
            var legacy = await secrets.GetSecretAsync("ApiKey");
            var scopedName = ProviderKeyStore.KeyFor(settingsService.Settings.AI.Provider);
            if (!string.IsNullOrEmpty(legacy) && !await secrets.SecretExistsAsync(scopedName))
                await secrets.StoreSecretAsync(scopedName, legacy);
            await secrets.DeleteSecretAsync("ApiKey");
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void RegisterActions()
    {
        var router = Services.GetRequiredService<ActionRouter>();
        var actions = Services.GetServices<IAction>();

        foreach (var action in actions)
        {
            router.RegisterAction(action);
        }
    }

    private void InitializeTrayIcon()
    {
        using (var iconResource = GetResourceStream(
                   new Uri("pack://application:,,,/Nexa;component/Assets/nexa.ico"))?.Stream)
        {
            if (iconResource != null)
                _appIcon = new SystemDrawing.Icon(iconResource);
        }
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = _appIcon ?? SystemDrawing.SystemIcons.Application,
            Text = "Nexa AI Assistant",
            Visible = true
        };

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Open Nexa", null, (s, e) => ShowCommandPalette());
        _microphoneMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        UpdateMicrophoneMenuItem();
        _microphoneMenuItem.Click += (s, e) => Dispatcher.BeginInvoke(() => _ = ToggleMicrophoneMuteAsync());
        contextMenu.Items.Add(_microphoneMenuItem);
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("Settings", null, (s, e) => ShowSettings());
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) => ExitApp());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowCommandPalette();
    }

    private void InitializeHotkey()
    {
        _hotkey = new GlobalHotkey(System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.Space);
        _hotkey.HotkeyPressed += (s, e) => ShowCommandPalette();
        _hotkey.Register();
    }

    private void ShowCommandPalette()
    {
        if (_commandPalette == null)
        {
            _commandPalette = Services.GetRequiredService<CommandPaletteWindow>();
            _commandPalette.Closed += (_, _) => _commandPalette = null;
        }
        _commandPalette.ShowAndFocus();
    }

    private void ExitApp()
    {
        IsShuttingDown = true;
        Shutdown();
    }

    private static void ActivateExistingInstance()
    {
        var processName = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "Nexa";
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(process.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
            catch { /* an older process may have exited while we looked for it */ }
            finally { process.Dispose(); }
        }
    }

    public void ShowSettings(bool openGuide = false)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = Services.GetRequiredService<Windows.SettingsWindow>();
            if (_commandPalette?.IsVisible == true)
                _settingsWindow.Owner = _commandPalette;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (openGuide)
            _settingsWindow.ShowGuide();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ApplyLaunchAtStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (key == null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
                key.SetValue("Nexa", $"\"{exe}\"");
        }
        else if (key.GetValue("Nexa") != null)
            key.DeleteValue("Nexa", throwOnMissingValue: false);
    }

    public void ApplyTheme(string? theme)
    {
        var light = string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        SetSolidBrush("BackgroundBrush", light ? "#F4F7FC" : "#0B1223");
        SetSolidBrush("SurfaceBrush", light ? "#FFFFFF" : "#17223A");
        SetSolidBrush("BorderBrush", light ? "#D6DEED" : "#30405B");
        SetSolidBrush("TextBrush", light ? "#142039" : "#F7F9FF");
        SetSolidBrush("SecondaryTextBrush", light ? "#5D6B82" : "#A9BAD6");
        SetSolidBrush("AccentBrush", light ? "#087F83" : "#5BDDD9");

        var hero = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        hero.GradientStops.Add(new GradientStop(ParseColor(light ? "#E9E7FF" : "#263464"), 0));
        hero.GradientStops.Add(new GradientStop(ParseColor(light ? "#E7F0FF" : "#1B2C4D"), .55));
        hero.GradientStops.Add(new GradientStop(ParseColor(light ? "#E3F7F5" : "#12363E"), 1));
        Resources["HeroBrush"] = hero;
    }

    private void SetSolidBrush(string key, string color)
    {
        // Resource brushes are frozen in optimized Release builds. Replacing the
        // resource also lets windows created after a theme switch resolve the new value.
        Resources[key] = new SolidColorBrush(ParseColor(color));
    }

    private static System.Windows.Media.Color ParseColor(string value) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!;

    public void ClearConversationHistory() => _mainViewModel?.ClearConversationHistory();

    public void NotifySettingsChanged() => _mainViewModel?.NotifySettingsChanged();

    public async Task RefreshWakeWordListenerAsync()
    {
        if (_wakeListener == null) return;
        var voice = Services.GetRequiredService<ISettingsService>().Settings.Voice;
        if (voice.MicrophoneMuted || !voice.WakeWordEnabled)
        {
            await _wakeListener.PauseAsync();
            return;
        }
        ResumeWakeListener();
    }

    public Task PauseWakeListenerAsync() => _wakeListener?.PauseAsync() ?? Task.CompletedTask;

    public void ResumeWakeListener()
    {
        if (_wakeListener == null || _wakeHandling || _mainViewModel?.SessionActive == true) return;
        var voice = Services.GetRequiredService<ISettingsService>().Settings.Voice;
        if (!voice.MicrophoneMuted && voice.WakeWordEnabled &&
            !_wakeListener.TryStart() && _mainViewModel != null)
        {
            _mainViewModel.StatusMessage = "Local wake-word recognition is unavailable. Use Listen or Alt+Space.";
            _mainViewModel.ShowStatus = true;
        }
    }

    private void OnVoiceSessionEnded()
    {
        _wakeGlow?.Hide();
        ResumeWakeListener();
    }

    private void OnWakeWordDetected()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (Services.GetRequiredService<ISettingsService>().Settings.Voice.MicrophoneMuted ||
                _wakeHandling || _mainViewModel?.IsBusy == true) return;
            _wakeHandling = true;
            _ = HandleWakeAsync();
        });
    }

    private async Task HandleWakeAsync()
    {
        try
        {
            await PauseWakeListenerAsync();
            if (Services.GetRequiredService<ISettingsService>().Settings.Voice.MicrophoneMuted) return;
            ShowCommandPalette();
            _wakeGlow ??= new WakeGlowWindow();
            _wakeGlow.Show();
            _mainViewModel!.StatusMessage = "Yes — Listening...";
            _mainViewModel.ShowStatus = true;
            await Services.GetRequiredService<TextToSpeechService>().SpeakWakeAcknowledgementAsync();
            if (!Services.GetRequiredService<ISettingsService>().Settings.Voice.MicrophoneMuted &&
                _mainViewModel?.IsBusy == false)
                await _mainViewModel.StartVoiceSessionAsync();
        }
        catch (Exception ex)
        {
            if (_mainViewModel != null)
            {
                _mainViewModel.StatusMessage = $"Wake-word listening failed: {ex.Message}";
                _mainViewModel.ShowStatus = true;
            }
        }
        finally
        {
            _wakeGlow?.Hide();
            _wakeHandling = false;
            ResumeWakeListener();
        }
    }

    public async Task ToggleMicrophoneMuteAsync()
    {
        await _microphoneMuteGate.WaitAsync();
        try
        {
            var service = Services.GetRequiredService<ISettingsService>();
            var voice = service.Settings.Voice;
            voice.MicrophoneMuted = !voice.MicrophoneMuted;
            _mainViewModel?.NotifyMicrophoneMuteChanged();
            UpdateMicrophoneMenuItem();

            if (voice.MicrophoneMuted)
            {
                _settingsWindow?.CancelMicrophoneTest();
                await PauseWakeListenerAsync();
                _wakeGlow?.Hide();
                if (_mainViewModel != null)
                {
                    _mainViewModel.StatusMessage = "Microphone muted. Text chat is still available.";
                    _mainViewModel.ShowStatus = true;
                }
            }
            else
            {
                ResumeWakeListener();
                if (_mainViewModel != null)
                {
                    _mainViewModel.StatusMessage = "Microphone enabled.";
                    _mainViewModel.ShowStatus = true;
                }
            }

            await service.SaveAsync();
        }
        finally
        {
            _microphoneMuteGate.Release();
        }
    }

    private void UpdateMicrophoneMenuItem()
    {
        if (_microphoneMenuItem == null) return;
        var muted = Services.GetRequiredService<ISettingsService>().Settings.Voice.MicrophoneMuted;
        _microphoneMenuItem.Text = muted ? "Unmute microphone" : "Mute microphone";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mainViewModel != null)
        {
            _mainViewModel.BeforeVoiceSession -= PauseWakeListenerAsync;
            _mainViewModel.VoiceSessionEnded -= OnVoiceSessionEnded;
        }
        if (_wakeListener != null)
        {
            _wakeListener.WakeWordDetected -= OnWakeWordDetected;
            _wakeListener.Dispose();
        }
        _wakeGlow?.Close();
        _hotkey?.Unregister();
        _notifyIcon?.Dispose();
        _appIcon?.Dispose();
        if (_ownsSingleInstance) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _host?.Dispose();
        _microphoneMuteGate.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsShuttingDown = true;
        base.OnSessionEnding(e);
    }
}
