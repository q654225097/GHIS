using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.MedTech;

public partial class MedTechStationView : UserControl
{
    public MedTechStationView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is MedTechStationViewModel vm)
        {
            vm.FocusSearchAction = () => SearchBox.Focus();
            vm.Attach();
        }
        SearchBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is MedTechStationViewModel vm)
        {
            vm.Detach();
        }
    }

}

