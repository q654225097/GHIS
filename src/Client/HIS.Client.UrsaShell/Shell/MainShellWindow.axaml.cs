using System;
using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;
using HIS.Client.UrsaShell.Views;

namespace HIS.Client.UrsaShell.Shell;

public partial class MainShellWindow : Window
{
    public ShellViewModel ViewModel => ShellControl.ViewModel;

    public MainShellWindow() : this(new WorkbenchModel())
    {
    }

    public MainShellWindow(WorkbenchModel workbench)
    {
        InitializeComponent();
        ShellControl.InitializeWithWorkbench(workbench);
        ShellControl.LogoutRequested += () =>
        {
            var login = new LoginWindow();
            login.Show();
            Close();
        };

        Closed += (_, _) => ShellControl.DisposeShell();
    }
}
