using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public partial class RoleAdminStationView : UserControl
{
    public RoleAdminStationView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is RoleAdminStationViewModel vm)
        {
            vm.FocusSearchAction = () => ResourceSearchBox.Focus();
            vm.Attach();
        }
        ResourceSearchBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is RoleAdminStationViewModel vm)
        {
            vm.Detach();
        }
    }
}

