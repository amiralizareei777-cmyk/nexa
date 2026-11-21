using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using Nexa.App.Models;
using Nexa.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Nexa.App.Windows;

/// <summary>
/// Interaction logic for CommandPaletteWindow.xaml
/// </summary>
public partial class CommandPaletteWindow : Window
{
    private readonly MainViewModel _viewModel;
    private WindowState _stateBeforeFullScreen = WindowState.Normal;

    public CommandPaletteWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += CommandPaletteWindow_Loaded;
        _viewModel.RequestCommandFocus += ViewModel_RequestCommandFocus;
        _viewModel.RequestConfirmationFocus += ViewModel_RequestConfirmationFocus;
    }

    private void CommandPaletteWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CommandTextBox.Focus();
    }

    private void ViewModel_RequestCommandFocus()
    {
        if (IsVisible)
        {
            CommandTextBox.Focus();
        }
    }

    private void ViewModel_RequestConfirmationFocus()
    {
        ShowAndFocus();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _stateBeforeFullScreen;
            }
            else
            {
                _stateBeforeFullScreen = WindowState;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // If an operation is underway (listening or processing), cancel it.
            // Otherwise, minimize it so it remains available from the taskbar.
            if (_viewModel.SessionActive)
            {
                _viewModel.StopSessionCommand.Execute(null);
            }
            else if (_viewModel.IsBusy || _viewModel.HasPendingConfirmation)
            {
                _viewModel.CancelCommand.Execute(null);
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
            e.Handled = true;
        }
    }

    private void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewModel.ExecuteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
        => ((App)System.Windows.Application.Current).ShowSettings();

    private void Guide_Click(object sender, RoutedEventArgs e)
        => ((App)System.Windows.Application.Current).ShowSettings(openGuide: true);

    private async void MicrophoneMute_Click(object sender, RoutedEventArgs e)
        => await ((App)System.Windows.Application.Current).ToggleMicrophoneMuteAsync();

    private void ResultItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ResultItem result)
        {
            _viewModel.SelectResult(result);
        }
    }

    public void ShowAndFocus()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
        CommandTextBox.Focus();
        CommandTextBox.SelectAll();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!((App)System.Windows.Application.Current).IsShuttingDown)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.StopSessionCommand.Execute(null);
        _viewModel.CancelCommand.Execute(null);
        _viewModel.RequestCommandFocus -= ViewModel_RequestCommandFocus;
        _viewModel.RequestConfirmationFocus -= ViewModel_RequestConfirmationFocus;
        base.OnClosed(e);
    }
}
