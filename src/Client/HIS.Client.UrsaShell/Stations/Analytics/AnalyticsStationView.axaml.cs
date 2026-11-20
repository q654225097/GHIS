using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HIS.Client.UrsaShell.Stations.Analytics;

public partial class AnalyticsStationView : UserControl
{
    private AnalyticsStationViewModel? _vm;

    public AnalyticsStationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as AnalyticsStationViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            RenderResultsGrid();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnalyticsStationViewModel.TotalResultRows) ||
            e.PropertyName == nameof(AnalyticsStationViewModel.HasResults) ||
            e.PropertyName == nameof(AnalyticsStationViewModel.ExecutionErrorMessage))
        {
            RenderResultsGrid();
        }
    }

    private void RenderResultsGrid()
    {
        if (ResultsContainer == null) return;
        ResultsContainer.Children.Clear();

        if (_vm == null || !_vm.HasResults || _vm.ResultColumns.Count == 0)
        {
            var msg = _vm?.ExecutionErrorMessage;
            var emptyText = new TextBlock
            {
                Text = string.IsNullOrEmpty(msg)
                    ? "暂无查询数据，请在上方配置参数并点击【🚀 执行报表查询】"
                    : $"查询未返回数据: {msg}",
                Foreground = Application.Current?.FindResource("SemiColorText2") as IBrush ?? Brushes.Gray,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40)
            };
            ResultsContainer.Children.Add(emptyText);
            return;
        }

        var mainPanel = new StackPanel { Spacing = 0 };

        // 1. Header Row
        var headerBorder = new Border
        {
            Background = Application.Current?.FindResource("SemiColorBackground2") as IBrush ?? Brushes.LightGray,
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Padding = new Thickness(14, 10),
            BorderBrush = Application.Current?.FindResource("SemiColorBorder") as IBrush ?? Brushes.Gray,
            BorderThickness = new Thickness(1, 1, 1, 1)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition(45, GridUnitType.Pixel)); // #
        for (int i = 0; i < _vm.ResultColumns.Count; i++)
        {
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 140 });
        }

        var noHeader = new TextBlock
        {
            Text = "#",
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = Application.Current?.FindResource("SemiColorText2") as IBrush ?? Brushes.DarkGray,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(noHeader, 0);
        headerGrid.Children.Add(noHeader);

        for (int i = 0; i < _vm.ResultColumns.Count; i++)
        {
            var colText = new TextBlock
            {
                Text = _vm.ResultColumns[i],
                FontWeight = FontWeight.Bold,
                FontSize = 12,
                Foreground = Application.Current?.FindResource("SemiColorText0") as IBrush ?? Brushes.Black,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(colText, i + 1);
            headerGrid.Children.Add(colText);
        }
        headerBorder.Child = headerGrid;
        mainPanel.Children.Add(headerBorder);

        // 2. Data Rows
        var bg0 = Application.Current?.FindResource("SemiColorBackground0") as IBrush ?? Brushes.White;
        var bg1 = Application.Current?.FindResource("SemiColorBackground1") as IBrush ?? Brushes.WhiteSmoke;
        var borderBrush = Application.Current?.FindResource("SemiColorBorder") as IBrush ?? Brushes.LightGray;

        for (int r = 0; r < _vm.ResultRows.Count; r++)
        {
            var rowData = _vm.ResultRows[r];
            var rowBorder = new Border
            {
                Background = (r % 2 == 0) ? bg0 : bg1,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1, 0, 1, 1),
                Padding = new Thickness(14, 8)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition(45, GridUnitType.Pixel));
            for (int i = 0; i < _vm.ResultColumns.Count; i++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 140 });
            }

            var rowNum = new TextBlock
            {
                Text = (r + 1).ToString(),
                FontSize = 11,
                Foreground = Application.Current?.FindResource("SemiColorText2") as IBrush ?? Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(rowNum, 0);
            rowGrid.Children.Add(rowNum);

            for (int c = 0; c < _vm.ResultColumns.Count; c++)
            {
                string val = (c < rowData.Count) ? rowData[c] : string.Empty;
                var cell = new SelectableTextBlock
                {
                    Text = val,
                    FontSize = 12,
                    Foreground = Application.Current?.FindResource("SemiColorText1") as IBrush ?? Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(cell, c + 1);
                rowGrid.Children.Add(cell);
            }

            rowBorder.Child = rowGrid;
            mainPanel.Children.Add(rowBorder);
        }

        ResultsContainer.Children.Add(mainPanel);
    }
}
