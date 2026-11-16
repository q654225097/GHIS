using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HIS.Client.UrsaShell.Shell.Controls;

public partial class StatusTag : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusTag, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<StatusTag, string>(nameof(Kind), "Neutral");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public StatusTag()
    {
        InitializeComponent();
        ApplyKind(Kind);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            ApplyKind(change.GetNewValue<string>());
        }
    }

    private void ApplyKind(string? kind)
    {
        var (bgKey, fgKey, borderKey) = (kind ?? "Neutral") switch
        {
            "Success" => ("SemiColorSuccessLight", "SemiColorSuccess", "SemiColorSuccess"),
            "Warning" => ("SemiColorWarningLight", "SemiColorWarning", "SemiColorWarning"),
            "Danger" => ("SemiColorDangerLight", "SemiColorDanger", "SemiColorDanger"),
            "Info" or "Primary" => ("SemiColorPrimaryLight", "SemiColorPrimary", "SemiColorPrimary"),
            _ => ("SemiColorBackground2", "SemiColorText1", "SemiColorBorder")
        };

        if (Application.Current?.TryGetResource(bgKey, ActualThemeVariant, out var bgObj) == true && bgObj is IBrush bg)
        {
            TagBorder.Background = bg;
        }
        if (Application.Current?.TryGetResource(borderKey, ActualThemeVariant, out var bdObj) == true && bdObj is IBrush bd)
        {
            TagBorder.BorderBrush = bd;
        }
        if (Application.Current?.TryGetResource(fgKey, ActualThemeVariant, out var fgObj) == true && fgObj is IBrush fg)
        {
            TagText.Foreground = fg;
        }
    }
}

