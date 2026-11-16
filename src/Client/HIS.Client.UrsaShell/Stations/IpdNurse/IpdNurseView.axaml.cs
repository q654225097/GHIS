using Avalonia;
using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdNurse;

public partial class IpdNurseView : UserControl
{
    public IpdNurseView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is IpdNurseViewModel vm)
        {
            vm.FocusVitalsInputAction = () => VitalsTempBox.Focus();
            vm.FocusHandoverInputAction = () => HandoverInputBox.Focus();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is IpdNurseViewModel vm)
        {
            vm.Attach();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is IpdNurseViewModel vm)
        {
            vm.Detach();
        }
        base.OnDetachedFromVisualTree(e);
    }

}

