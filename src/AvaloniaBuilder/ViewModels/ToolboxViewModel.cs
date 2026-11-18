using System.Collections.ObjectModel;
using AvaloniaBuilder.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaBuilder.ViewModels;

public partial class ToolboxGroupViewModel : ObservableObject
{
	public ToolboxGroupViewModel(string name, IEnumerable<ToolboxItem> items, bool isExpanded)
	{
		Name = name;
		Items = new ObservableCollection<ToolboxItem>(items);
		IsExpanded = isExpanded;
	}

	public string Name { get; }

	public ObservableCollection<ToolboxItem> Items { get; }

	[ObservableProperty]
	public partial bool IsExpanded { get; set; }
}

public partial class ToolboxViewModel : ObservableObject
{
	private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
	private bool _filtered;

	public ToolboxViewModel()
	{
		Rebuild();
	}

	public ObservableCollection<ToolboxGroupViewModel> Groups { get; } = new();

	[ObservableProperty]
	public partial string Filter { get; set; } = "";

	partial void OnFilterChanged(string value) => Rebuild();

	private void Rebuild()
	{
		if (!_filtered)
			foreach (var group in Groups)
				_expanded[group.Name] = group.IsExpanded;
		Groups.Clear();

		string filter = Filter.Trim();
		_filtered = filter.Length > 0;
		foreach (string category in ToolboxCatalog.Categories)
		{
			var items = ToolboxCatalog.Items
				.Where(i => i.Category == category)
				.Where(i => filter.Length == 0 || i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (items.Count == 0)
				continue;
			bool expanded = filter.Length > 0 || _expanded.GetValueOrDefault(category, true);
			Groups.Add(new ToolboxGroupViewModel(category, items, expanded));
		}
	}
}
