using System;
using Avalonia.Controls;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class MainHostView : UserControl
{
    private LoginView? _loginView;
    private MainShellView? _shellView;

    public MainHostView()
    {
        InitializeComponent();
        ShowLogin();
    }

    public void ShowLogin()
    {
        _loginView = new LoginView();
        _loginView.LoginSucceeded += OnLoginSucceeded;
        MainContentHost.Content = _loginView;
    }

    private void OnLoginSucceeded()
    {
        var workbench = new WorkbenchModel();
        _shellView = new MainShellView(workbench);
        _shellView.LogoutRequested += () =>
        {
            SessionContext.Current.Logout();
            ShowLogin();
        };
        MainContentHost.Content = _shellView;
    }
}
