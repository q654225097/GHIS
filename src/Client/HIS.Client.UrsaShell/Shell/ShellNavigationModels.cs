using System.Collections.ObjectModel;
using System.Windows.Input;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.Shell;

public sealed class ShellNavGroup : ClientModelBase
{
    private bool _isExpanded = true;

    public ShellNavGroup(string key, string title, bool isExpanded = true)
    {
        Key = key;
        Title = title;
        _isExpanded = isExpanded;
    }

    public string Key { get; }
    public string Title { get; }
    public ObservableCollection<ShellNavItem> Items { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value, nameof(IsExpanded));
    }
}

public sealed class ShellNavItem : ClientModelBase
{
    private int _badgeCount;
    private bool _isSelected;

    public string Key { get; init; } = string.Empty;
    public int LegacyNavIndex { get; init; } = -1;
    public string Title { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public bool IsClinical { get; init; }
    public string Description { get; init; } = string.Empty;

    public int BadgeCount
    {
        get => _badgeCount;
        set
        {
            if (SetField(ref _badgeCount, value, nameof(BadgeCount)))
            {
                OnPropertyChanged(nameof(HasBadge));
                OnPropertyChanged(nameof(BadgeText));
            }
        }
    }

    public bool HasBadge => BadgeCount > 0;
    public string BadgeText => BadgeCount > 99 ? "99+" : BadgeCount.ToString();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value, nameof(IsSelected));
    }
}

public sealed class GlobalPatientHit
{
    public string ScopeLabel { get; init; } = string.Empty;
    public string DisplayTitle { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public bool IsInpatient { get; init; }
    public object PatientModel { get; init; } = null!;
}

