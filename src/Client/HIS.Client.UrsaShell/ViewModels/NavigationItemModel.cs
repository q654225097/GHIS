using System;
using System.Collections.ObjectModel;
using HIS.Client.Core;

namespace HIS.Client.UrsaShell.ViewModels;

public record NavigationItemModel(int TargetIndex, string Title, string Icon)
{
	public string Key { get; init; } = TargetIndex.ToString();

	public string Group { get; init; } = string.Empty;
}

public sealed class NavigationGroupModel : ClientModelBase
{
	private bool _isExpanded;

	public NavigationGroupModel(string key, string title, bool isExpanded = true)
	{
		Key = key;
		Title = title;
		_isExpanded = isExpanded;
	}

	public string Key { get; }

	public string Title { get; }

	public ObservableCollection<NavigationItemModel> Items { get; } = new ObservableCollection<NavigationItemModel>();

	public bool IsExpanded
	{
		get => _isExpanded;
		set => SetField(ref _isExpanded, value, nameof(IsExpanded));
	}
}
