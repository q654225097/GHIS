using Avalonia;
using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.IpdDoctor;

public partial class IpdDoctorView : UserControl
{
    public IpdDoctorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is IpdDoctorViewModel vm)
        {
            vm.FocusOrderInputAction = () => OrderContentInput.Focus();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is IpdDoctorViewModel vm)
        {
            vm.Attach();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is IpdDoctorViewModel vm)
        {
            vm.Detach();
        }
        base.OnDetachedFromVisualTree(e);
    }

}

