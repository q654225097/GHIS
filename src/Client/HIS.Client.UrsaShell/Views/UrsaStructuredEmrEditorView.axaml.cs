using Avalonia.Controls;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class UrsaStructuredEmrEditorView : UserControl
{
    public UrsaStructuredEmrEditorView()
    {
        InitializeComponent();
        var vm = new EmrEditorWorkspaceModel();
        DataContext = vm;

        BtnSignOfd.Click += async (s, e) =>
        {
            await vm.SignOfdAsync();
        };

        BtnExportOfd.Click += async (s, e) =>
        {
            await vm.ExportOfdAsync();
        };
    }
}

