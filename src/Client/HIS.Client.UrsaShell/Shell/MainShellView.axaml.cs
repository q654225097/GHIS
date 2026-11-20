using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HIS.Client.Core;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Client.UrsaShell.Views;

namespace HIS.Client.UrsaShell.Shell;

public partial class MainShellView : UserControl
{
    private bool _isDisposed;
    public ShellViewModel ViewModel { get; private set; }

    public event Action? LogoutRequested;

    public MainShellView() : this(new WorkbenchModel())
    {
    }

    public MainShellView(WorkbenchModel workbench)
    {
        InitializeComponent();
        ViewModel = new ShellViewModel(workbench);
        DataContext = ViewModel;

        ViewModel.AttachWindowCallbacks(
            openLegacyWindow: OpenLegacyCompatibilityWindow,
            logoutCallback: PerformLogout,
            focusSearchCallback: FocusGlobalSearchBox);

        KeyDown += Shell_KeyDown;
        DetachedFromVisualTree += (_, _) => DisposeShell();
    }

    public void InitializeWithWorkbench(WorkbenchModel workbench)
    {
        ViewModel = new ShellViewModel(workbench);
        DataContext = ViewModel;

        ViewModel.AttachWindowCallbacks(
            openLegacyWindow: OpenLegacyCompatibilityWindow,
            logoutCallback: PerformLogout,
            focusSearchCallback: FocusGlobalSearchBox);
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
            Key.F1 => "F1",
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

        if (gesture == "/" && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
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
        LogoutRequested?.Invoke();
    }

    public void DisposeShell()
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
