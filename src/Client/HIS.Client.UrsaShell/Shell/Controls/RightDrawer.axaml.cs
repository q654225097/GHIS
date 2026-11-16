using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace HIS.Client.UrsaShell.Shell.Controls;

public partial class RightDrawer : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<RightDrawer, bool>(nameof(IsOpen));

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<RightDrawer, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<RightDrawer, string?>(nameof(Subtitle));

    public static readonly StyledProperty<double> DrawerWidthProperty =
        AvaloniaProperty.Register<RightDrawer, double>(nameof(DrawerWidth), 520);

    public static readonly StyledProperty<object?> DrawerContentProperty =
        AvaloniaProperty.Register<RightDrawer, object?>(nameof(DrawerContent));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<RightDrawer, ICommand?>(nameof(CloseCommand));

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public double DrawerWidth { get => GetValue(DrawerWidthProperty); set => SetValue(DrawerWidthProperty, value); }
    public object? DrawerContent { get => GetValue(DrawerContentProperty); set => SetValue(DrawerContentProperty, value); }
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }

    public RightDrawer()
    {
        InitializeComponent();
    }

    private void Backdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CloseCommand?.CanExecute(null) == true)
        {
            CloseCommand.Execute(null);
        }
        else
        {
            IsOpen = false;
        }
    }
}

