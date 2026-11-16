using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HIS.Client.UrsaShell.Stations.OpdDoctor;

public partial class OpdDoctorView : UserControl
{
    public OpdDoctorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is OpdDoctorViewModel vm)
            {
                vm.AttachFocusDrugSearch(() =>
                {
                    DrugSearchBox.Focus();
                    DrugSearchBox.SelectAll();
                });
            }
        };
    }

    private void DrugSearchBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OpdDoctorViewModel vm)
        {
            vm.RefreshDrugSearchResults();
            vm.IsDrugDropdownOpen = vm.FilteredDrugCatalog.Count > 0;
        }
    }

    private void DrugSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not OpdDoctorViewModel vm) return;

        if (e.Key == Key.Down)
        {
            vm.MoveDrugSelection(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            vm.MoveDrugSelection(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            vm.AddSelectedCatalogDrug();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.IsDrugDropdownOpen = false;
            e.Handled = true;
        }
    }
}

