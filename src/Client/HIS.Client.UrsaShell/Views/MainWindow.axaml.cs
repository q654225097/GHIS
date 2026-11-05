using Ursa.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class MainWindow : UrsaWindow
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new WorkbenchModel();
    }
}

