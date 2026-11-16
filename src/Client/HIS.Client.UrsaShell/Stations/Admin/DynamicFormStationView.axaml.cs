using Avalonia.Controls;

namespace HIS.Client.UrsaShell.Stations.Admin;

public partial class DynamicFormStationView : UserControl
{
    public DynamicFormStationView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is DynamicFormStationViewModel vm)
        {
            vm.FocusSearchAction = () => SearchBox.Focus();
            vm.Attach();
        }
        SearchBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is DynamicFormStationViewModel vm)
        {
            vm.Detach();
        }
    }

}

