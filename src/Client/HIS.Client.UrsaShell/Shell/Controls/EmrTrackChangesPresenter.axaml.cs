using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HIS.Client.UrsaShell.Shell.Controls;

public partial class EmrTrackChangesPresenter : UserControl
{
    public bool IsTrackMode { get; private set; } = true;

    public EmrTrackChangesPresenter()
    {
        InitializeComponent();
    }

    private void OnTrackModeClick(object? sender, RoutedEventArgs e)
    {
        IsTrackMode = true;
        BtnTrackMode.Classes.Set("Primary", true);
        BtnTrackMode.Classes.Set("Secondary", false);
        BtnCleanMode.Classes.Set("Primary", false);
        BtnCleanMode.Classes.Set("Secondary", true);
    }

    private void OnCleanModeClick(object? sender, RoutedEventArgs e)
    {
        IsTrackMode = false;
        BtnTrackMode.Classes.Set("Primary", false);
        BtnTrackMode.Classes.Set("Secondary", true);
        BtnCleanMode.Classes.Set("Primary", true);
        BtnCleanMode.Classes.Set("Secondary", false);
    }
}
