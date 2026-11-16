using Avalonia.Controls;
using HIS.Client.UrsaShell.Shell;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

/// <summary>
/// Stage 7 minimal compatibility shell kept solely for fallback callers.
/// All clinical, pharmacy, finance, schedule, and admin workflows now live under Stations/*.
/// </summary>
public partial class MainWindow : Window
{
    private MainShellWindow? _nextShell;

    public MainWindow() : this(new WorkbenchModel())
    {
    }

    public MainWindow(WorkbenchModel workbench)
    {
        InitializeComponent();
        DataContext = workbench;
        workbench.OpenWorkspaceCommand = new RelayCommand(() => OpenWorkspace());
        workbench.CloseWindowCommand = new RelayCommand(() => Close());
    }

    private void OpenWorkspace()
    {
        if (DataContext is WorkbenchModel vm)
        {
            var shell = new MainShellWindow(vm);
            _nextShell = shell;
            Closed -= ReleaseNextShell;
            Closed += ReleaseNextShell;
            shell.Show();
            Close();
        }
    }

    private void ReleaseNextShell(object? sender, System.EventArgs e)
    {
        _nextShell = null;
    }
}
