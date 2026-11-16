using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Admin;

public partial class AuditLogStationView : UserControl
{
    public AuditLogStationView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is AuditLogStationViewModel vm)
        {
            vm.FocusFilterAction = () => EventFilterBox.Focus();
            vm.Attach();
        }
        EventFilterBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is AuditLogStationViewModel vm)
        {
            vm.Detach();
        }
    }

}

