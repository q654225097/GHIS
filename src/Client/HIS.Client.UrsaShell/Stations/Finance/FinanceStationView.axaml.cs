using Avalonia;
using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.Finance;

public partial class FinanceStationView : UserControl
{
    public FinanceStationView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is FinanceStationViewModel vm)
        {
            vm.Attach();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is FinanceStationViewModel vm)
        {
            vm.Detach();
        }
        base.OnDetachedFromVisualTree(e);
    }

}

