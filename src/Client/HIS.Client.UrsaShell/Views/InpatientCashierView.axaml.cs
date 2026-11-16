using System;
using Avalonia.Controls;
using Avalonia.Threading;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Views;

public partial class InpatientCashierView : UserControl
{
    private readonly InpatientCashierViewModel _model = new();
    private bool _loadRequested;

    public InpatientCashierView()
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
                await _model.EnsureLoadedAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }, DispatcherPriority.Loaded);
    }
}
