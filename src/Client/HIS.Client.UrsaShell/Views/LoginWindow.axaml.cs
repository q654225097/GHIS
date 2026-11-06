using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class LoginWindow : Window
{
    private readonly LoginModel _model;

    public LoginWindow()
    {
        InitializeComponent();
        _model = new LoginModel();
        DataContext = _model;
    }

    private void UserCard_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string userName)
        {
            var user = _model.KnownUsers.FirstOrDefault(u => u.UserName == userName);
            if (user != null) _model.SelectedUser = user;
        }
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        await DoLogin();
    }

    private async void Password_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await DoLogin();
    }

    private async System.Threading.Tasks.Task DoLogin()
    {
        var success = await _model.LoginAsync();
        if (success)
        {
            var main = new MainWindow();
            main.Show();
            Close();
        }
    }
}

