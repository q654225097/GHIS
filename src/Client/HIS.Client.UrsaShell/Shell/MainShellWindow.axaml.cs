using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HIS.Client.Core;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Client.UrsaShell.Views;

namespace HIS.Client.UrsaShell.Shell;

public partial class MainShellWindow : Window
{
    private bool _isDisposed;

    public ShellViewModel ViewModel { get; }

    public MainShellWindow() : this(new WorkbenchModel())
    {
    }

    public MainShellWindow(WorkbenchModel workbench)
    {
        InitializeComponent();
        ViewModel = new ShellViewModel(workbench);
        DataContext = ViewModel;

        ViewModel.AttachWindowCallbacks(
            openLegacyWindow: OpenLegacyCompatibilityWindow,
            logoutCallback: PerformLogout,
            focusSearchCallback: FocusGlobalSearchBox);

        KeyDown += Shell_KeyDown;
        Closed += (_, _) => DisposeShell();
    }

    private void FocusGlobalSearchBox()
    {
        ViewModel.RefreshGlobalSearchResults();
        ViewModel.IsSearchDropdownOpen = true;
        GlobalSearchBox.Focus();
        GlobalSearchBox.SelectAll();
    }

    private void GlobalSearchBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshGlobalSearchResults();
        ViewModel.IsSearchDropdownOpen = true;
    }

    private void GlobalSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ViewModel.IsSearchDropdownOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ViewModel.GlobalSearchResults.Count > 0)
        {
            ViewModel.SelectGlobalPatient(ViewModel.GlobalSearchResults[0]);
            e.Handled = true;
        }
    }

    private void Shell_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (ViewModel.IsConfirmOpen)
            {
                ViewModel.CloseConfirm();
                e.Handled = true;
                return;
            }
            if (ViewModel.IsDrawerOpen)
            {
                ViewModel.CloseDrawer();
                e.Handled = true;
                return;
            }
            if (ViewModel.IsSearchDropdownOpen)
            {
                ViewModel.IsSearchDropdownOpen = false;
                e.Handled = true;
                return;
            }
        }

        string? gesture = e.Key switch
        {
            Key.F2 => "F2",
            Key.OemQuestion or Key.Divide when e.KeyModifiers == KeyModifiers.None => "/",
            Key.S when e.KeyModifiers == KeyModifiers.Control => "Ctrl+S",
            Key.Enter when e.KeyModifiers == KeyModifiers.Control => "Ctrl+Enter",
            Key.Escape => "Esc",
            _ => null
        };

        if (gesture == null)
        {
            return;
        }

        // Do not hijack "/" when user is already typing inside a TextBox unless handled cleanly by station
        if (gesture == "/" && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        if (ViewModel.CurrentStationView is IShortcutTarget viewTarget && viewTarget.OnShortcut(gesture))
        {
            e.Handled = true;
        }
        else if (ViewModel.CurrentStationView is Control c && c.DataContext is IShortcutTarget vmTarget && vmTarget.OnShortcut(gesture))
        {
            e.Handled = true;
        }
    }

    private void OpenLegacyCompatibilityWindow(int navIndex)
    {
        ViewModel.Workbench.SelectedNavIndex = navIndex;
        var legacy = new MainWindow(ViewModel.Workbench);
        legacy.Show();
    }

    private void PerformLogout()
    {
        SessionContext.Current.Logout();
        var login = new LoginWindow();
        login.Show();
        Close();
    }

    private void DisposeShell()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        KeyDown -= Shell_KeyDown;
        ViewModel.Dispose();
    }
}
