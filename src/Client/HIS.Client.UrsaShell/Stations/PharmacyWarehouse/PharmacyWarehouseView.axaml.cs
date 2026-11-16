using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace HIS.Client.UrsaShell.Stations.PharmacyWarehouse;

public partial class PharmacyWarehouseView : UserControl
{
    private bool _scopeInitialized;

    public PharmacyWarehouseView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PharmacyWarehouseViewModel vm)
        {
            vm.FocusDrugSearchAction = () => DrugSearchBox.Focus();
            vm.ClearDrugListSelectionAction = () => DrugList.SelectedItem = null;
            _scopeInitialized = vm.Pharmacy.SelectedWarehouse != null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PharmacyWarehouseViewModel vm)
        {
            _ = InitAsync(vm);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is PharmacyWarehouseViewModel vm)
        {
            vm.OnDetached();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private async Task InitAsync(PharmacyWarehouseViewModel vm)
    {
        await vm.OnAttachedAsync();
        _scopeInitialized = true;
    }

    private async void WarehouseScope_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_scopeInitialized || DataContext is not PharmacyWarehouseViewModel vm)
        {
            return;
        }
        await vm.OnWarehouseChangedAsync();
    }

    private void DrugSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is PharmacyWarehouseViewModel vm && vm.SearchDrugsCommand.CanExecute(null))
        {
            vm.SearchDrugsCommand.Execute(null);
            e.Handled = true;
        }
    }
}

