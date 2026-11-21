using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows;
using System.Windows.Interop;

namespace Nexa.App;

/// <summary>
/// Handles global hotkey registration using RegisterHotKey.
/// </summary>
public class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private readonly int _id;
    private readonly uint _modifiers;
    private readonly uint _key;
    private readonly Window _window;
    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkey(ModifierKeys modifiers, Key key)
    {
        _id = Random.Shared.Next(1, 1000);
        _modifiers = (uint)modifiers;
        _key = (uint)KeyInterop.VirtualKeyFromKey(key);

        _window = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            IsHitTestVisible = false,
            ShowActivated = false,
            Opacity = 0
        };

        _window.SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);

        if (!_registered)
        {
            _registered = RegisterHotKey(helper.Handle, _id, _modifiers, _key);
        }
    }

    public void Register()
    {
        if (_registered) return;

        _window.Show();
        _window.Visibility = Visibility.Hidden;

        var helper = new WindowInteropHelper(_window);
        if (helper.Handle != IntPtr.Zero && !_registered)
        {
            _registered = RegisterHotKey(helper.Handle, _id, _modifiers, _key);
        }
    }

    public void Unregister()
    {
        if (!_registered) return;

        var helper = new WindowInteropHelper(_window);
        if (helper.Handle != IntPtr.Zero)
        {
            UnregisterHotKey(helper.Handle, _id);
        }
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _window.Close();
    }
}