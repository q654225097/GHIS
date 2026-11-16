using Avalonia.Controls;
using Avalonia.Threading;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class InpatientSettlementView : UserControl
{
    private readonly InpatientSettlementViewModel _model = new();
    private bool _loadRequested;

    public InpatientSettlementView()
    {
        InitializeComponent();
        DataContext = _model;
        AttachedToVisualTree += (_, _) => QueueInitialLoad();
    }

    private void QueueInitialLoad()
    {
        if (_loadRequested)
        {
            return;
        }
        _loadRequested = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await _model.LoadAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }, DispatcherPriority.Loaded);
    }

}
