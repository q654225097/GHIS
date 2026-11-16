using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HIS.Client.UrsaShell.Shell.Controls;

public enum FlowStepState
{
    Pending,
    Active,
    Completed,
    Blocked
}

public sealed class FlowStepItem
{
    public int Index { get; init; }
    public string Title { get; init; } = string.Empty;
    public FlowStepState State { get; init; } = FlowStepState.Pending;
    public string? Tooltip { get; init; }
    public bool IsLast { get; init; }
    public ICommand? Command { get; init; }

    public string BadgeText => State == FlowStepState.Completed ? "✓" : Index.ToString();

    public FontWeight TitleWeight => State == FlowStepState.Active ? FontWeight.Bold : FontWeight.Normal;

    public IBrush BadgeBackground => ResolveBrush(State switch
    {
        FlowStepState.Completed => "SemiColorSuccess",
        FlowStepState.Active => "SemiColorPrimary",
        FlowStepState.Blocked => "SemiColorWarning",
        _ => "SemiColorText3"
    });

    public IBrush TitleBrush => ResolveBrush(State switch
    {
        FlowStepState.Completed => "SemiColorSuccess",
        FlowStepState.Active => "SemiColorPrimary",
        FlowStepState.Blocked => "SemiColorWarning",
        _ => "SemiColorText2"
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

public partial class FlowStepBar : UserControl
{
    public static readonly StyledProperty<IEnumerable<FlowStepItem>?> StepsProperty =
        AvaloniaProperty.Register<FlowStepBar, IEnumerable<FlowStepItem>?>(nameof(Steps));

    public static readonly StyledProperty<string?> BlockedReasonProperty =
        AvaloniaProperty.Register<FlowStepBar, string?>(nameof(BlockedReason));

    public IEnumerable<FlowStepItem>? Steps
    {
        get => GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public string? BlockedReason
    {
        get => GetValue(BlockedReasonProperty);
        set => SetValue(BlockedReasonProperty, value);
    }

    public bool HasBlockedReason => !string.IsNullOrWhiteSpace(BlockedReason);

    public FlowStepBar()
    {
        InitializeComponent();
    }
}
