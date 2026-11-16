using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HIS.Client.UrsaShell.ViewModels;

namespace HIS.Client.UrsaShell.Stations.PharmacyWindow;

public partial class PharmacyWindowView : UserControl
{
    private bool _scopeInitialized;

    public PharmacyWindowView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PharmacyWindowViewModel vm)
        {
            vm.FocusSearchBoxAction = () => PrescriptionSearchBox.Focus();
            _scopeInitialized = vm.Pharmacy.SelectedPharmacy != null;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PharmacyWindowViewModel vm)
        {
            _ = InitAsync(vm);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is PharmacyWindowViewModel vm)
        {
            vm.OnDetached();
        }
        base.OnDetachedFromVisualTree(e);
    }

    private async Task InitAsync(PharmacyWindowViewModel vm)
    {
        await vm.OnAttachedAsync();
        _scopeInitialized = true;
    }

    private async void PharmacyScope_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_scopeInitialized || DataContext is not PharmacyWindowViewModel vm)
        {
            return;
        }
        await vm.OnScopePharmacyChangedAsync();
    }

    private async void DispenseRecord_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is PharmacyWindowViewModel vm && sender is ListBox list)
        {
            await vm.SelectDispenseAsync(list.SelectedItem as DispenseRow);
        }
    }

    private void WardFilter_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is PharmacyWindowViewModel vm && vm.FilterWardCommand.CanExecute(null))
        {
            vm.FilterWardCommand.Execute(null);
            e.Handled = true;
        }
    }
}

