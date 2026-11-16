using System;
using System.Windows.Input;
using Avalonia.Controls;
using HIS.Client.UrsaShell.Shell;

namespace HIS.Client.UrsaShell.Shell.Controls;

public partial class LegacyStationBridgeView : UserControl
{
    public static readonly Avalonia.StyledProperty<ICommand?> OpenCommandProperty =
        Avalonia.AvaloniaProperty.Register<LegacyStationBridgeView, ICommand?>(nameof(OpenCommand));

    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    public LegacyStationBridgeView()
    {
        InitializeComponent();
        OpenCommand = new RelayCommand(() => { });
    }

    public LegacyStationBridgeView(string title, string description, Action openAction)
    {
        InitializeComponent();
        TitleText.Text = title;
        DescText.Text = description;
        OpenCommand = new RelayCommand(openAction);
    }
}

