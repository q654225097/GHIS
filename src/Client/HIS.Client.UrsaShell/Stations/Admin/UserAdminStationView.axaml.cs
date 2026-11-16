using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public partial class UserAdminStationView : UserControl
{
    public UserAdminStationView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is UserAdminStationViewModel vm)
        {
            vm.FocusSearchAction = () => SearchBox.Focus();
            vm.Attach();
        }
        SearchBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is UserAdminStationViewModel vm)
        {
            vm.Detach();
        }
    }

}

