using Avalonia;
using Avalonia.Media;

namespace HIS.Client.UrsaShell.Shell;

public static class ThemeBrushes
{
    public static IBrush Get(string resourceKey, IBrush fallback)
    {
        if (Application.Current?.TryGetResource(resourceKey, Application.Current.ActualThemeVariant, out object? value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    public static IBrush Text1 => Get("SemiColorText1", Brushes.Gray);

    public static IBrush Text2 => Get("SemiColorText2", Brushes.DimGray);

    public static IBrush Text3 => Get("SemiColorText3", Brushes.Gray);

    public static IBrush Border => Get("SemiColorBorder", Brushes.LightGray);

    public static IBrush Surface => Get("SemiColorBackground1", Brushes.White);

    public static IBrush Primary => Get("SemiColorPrimary", Brushes.SteelBlue);

    public static IBrush PrimaryLight => Get("SemiColorPrimaryLight", Brushes.LightBlue);

    public static IBrush Success => Get("SemiColorSuccess", Brushes.SeaGreen);

    public static IBrush SuccessLight => Get("SemiColorSuccessLight", Brushes.Honeydew);

    public static IBrush Warning => Get("SemiColorWarning", Brushes.DarkGoldenrod);

    public static IBrush WarningLight => Get("SemiColorWarningLight", Brushes.LightYellow);

    public static IBrush Danger => Get("SemiColorDanger", Brushes.Firebrick);

    public static IBrush DangerLight => Get("SemiColorDangerLight", Brushes.MistyRose);
}
