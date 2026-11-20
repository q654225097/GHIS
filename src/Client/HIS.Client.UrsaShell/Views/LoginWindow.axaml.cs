using System;
using Avalonia.Controls;
using Avalonia.Threading;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class LoginWindow : Window
{
    private bool _hasNavigated;

    public LoginModel Model => LoginControl.Model;

    public LoginWindow()
    {
        InitializeComponent();
        LoginControl.LoginSucceeded += OnLoginSucceeded;
    }

    private void OnLoginSucceeded()
    {
        if (_hasNavigated)
        {
            return;
        }

        _hasNavigated = true;
        Dispatcher.UIThread.Post(() =>
        {
            Window next = ShellConfig.UseNewShell
                ? new MainShellWindow(new WorkbenchModel())
                : new MainWindow(new WorkbenchModel());
            next.Show();
            Close();
        });
    }
}
