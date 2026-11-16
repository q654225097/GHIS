using Avalonia;
using Avalonia.Controls;

namespace HIS.Client.UrsaShell.Stations.Registration;

public partial class RegistrationStationView : UserControl
{
    public RegistrationStationView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is RegistrationStationViewModel vm)
        {
            vm.Activate();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is RegistrationStationViewModel vm)
        {
            vm.Deactivate();
        }
        base.OnDetachedFromVisualTree(e);
    }
}
