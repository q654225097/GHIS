using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HIS.Client.UrsaShell.Shell.Controls;

public sealed class ToastMessage
{
    public string Id { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Severity { get; init; } = "Info";
    public string Timestamp { get; init; } = string.Empty;
    public string? ActionText { get; init; }
    public ICommand? ActionCommand { get; init; }
    public ICommand? DismissCommand { get; init; }

    public IBrush AccentBrush => ResolveBrush(Severity switch
    {
        "Success" => "SemiColorSuccess",
        "Warning" => "SemiColorWarning",
        "Danger" => "SemiColorDanger",
        _ => "SemiColorPrimary"
    });

    public IBrush BorderBrush => ResolveBrush(Severity switch
    {
        "Success" => "SemiColorSuccess",
        "Warning" => "SemiColorWarning",
        "Danger" => "SemiColorDanger",
        _ => "SemiColorBorder"
    });

    private static IBrush ResolveBrush(string key)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var res) == true &&
            res is IBrush brush)
        {
            return brush;
        }
        return Brushes.Gray;
    }
}

public partial class ToastHost : UserControl
{
    public static readonly StyledProperty<IEnumerable<ToastMessage>?> ItemsProperty =
        AvaloniaProperty.Register<ToastHost, IEnumerable<ToastMessage>?>(nameof(Items));

    public IEnumerable<ToastMessage>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public ToastHost()
    {
        InitializeComponent();
    }
}

