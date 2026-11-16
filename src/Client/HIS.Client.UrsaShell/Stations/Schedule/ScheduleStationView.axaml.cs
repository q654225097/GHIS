using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Schedule;

public partial class ScheduleStationView : UserControl
{
    public ScheduleStationView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is ScheduleStationViewModel vm)
        {
            vm.FocusTemplateNameAction = () => TemplateNameBox.Focus();
            vm.FocusQueryDateAction = () => QueryDateBox.Focus();
            vm.Attach();
        }
        QueryDateBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is ScheduleStationViewModel vm)
        {
            vm.Detach();
        }
    }

}
