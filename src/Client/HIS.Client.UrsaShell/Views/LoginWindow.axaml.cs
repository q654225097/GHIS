using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HIS.Client.Core;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class LoginWindow : Window
{
    private readonly LoginModel _model;
    private bool _isOpening = true;
    private bool _hasNavigated;

    public LoginWindow()
    {
        InitializeComponent();
        _model = new LoginModel();
        DataContext = _model;
        _model.LoginSucceeded += OnLoginSucceeded;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        PasswordBox.Focus();
        await _model.LoadContextsAsync();
        _isOpening = false;
        PasswordBox.Focus();
    }

    private async void AccountListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isOpening || _model.SelectedUser == null)
        {
            return;
        }

        if (_model.SelectedContext != null &&
            string.Equals(_model.SelectedContext.UserName, _model.SelectedUser.UserName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _model.SelectUserAsync(_model.SelectedUser);
        PasswordBox.Focus();
    }

    private async void Password_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (_model.CanLogin)
        {
            await _model.LoginAsync();
            if (!string.IsNullOrWhiteSpace(SessionContext.Current.AccessToken))
            {
                OnLoginSucceeded();
            }
        }
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
