using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.EmrArchive;

public partial class EmrArchiveView : UserControl
{
    public EmrArchiveView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is EmrArchiveViewModel vm)
        {
            vm.FocusSearchAction = () => SearchBox.Focus();
            vm.Attach();
        }
        SearchBox.Focus();
    }

    private void OnDetachedFromVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is EmrArchiveViewModel vm)
        {
            vm.Detach();
        }
    }

}

