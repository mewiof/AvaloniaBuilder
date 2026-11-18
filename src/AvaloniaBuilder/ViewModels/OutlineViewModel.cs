using System.Collections.ObjectModel;
using Avalonia;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Preview;
using AvaloniaBuilder.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaBuilder.ViewModels;

public enum DropPlace
{
	Before,
	Inside,
	After,
}

public sealed class OutlineRow
{
	public OutlineRow(DesignNode node, int depth, string detail, string? problem)
	{
		Node = node;
		Depth = depth;
		Detail = detail;
		Problem = problem;
		Title = node.ElementName;
		HasChildren = node.Children.Count > 0;
		IsExpanded = node.IsExpanded;
		IsPropertyElement = node.IsPropertyElement;
	}

	public DesignNode Node { get; }

	public int Depth { get; }

	public string Title { get; }

	public string Detail { get; }

	public string? Problem { get; }

	public bool HasProblem => Problem is not null;

	public bool HasChildren { get; }

	public bool IsExpanded { get; }

	public bool IsPropertyElement { get; }

	public Thickness Indent => new(Depth * 14, 0, 0, 0);

	public string Arrow => !HasChildren ? "" : IsExpanded ? "▾" : "▸";

	public double TitleOpacity => IsPropertyElement ? 0.6 : 1;

	public bool SameAs(OutlineRow other) =>
		ReferenceEquals(Node, other.Node) && Depth == other.Depth && Title == other.Title && Detail == other.Detail
		&& Problem == other.Problem && HasChildren == other.HasChildren && IsExpanded == other.IsExpanded;
}

public partial class OutlineViewModel : ObservableObject
{
	private DesignDocument? _document;
	private IReadOnlyList<PreviewDiagnostic> _diagnostics = Array.Empty<PreviewDiagnostic>();
	private bool _syncing;

	public ObservableCollection<OutlineRow> Rows { get; } = new();

	[ObservableProperty]
	public partial OutlineRow? SelectedRow { get; set; }

	public void SetDocument(DesignDocument? document)
	{
		_document = document;
		_diagnostics = Array.Empty<PreviewDiagnostic>();
		Refresh();
	}

	public void SetDiagnostics(IReadOnlyList<PreviewDiagnostic> diagnostics)
	{
		_diagnostics = diagnostics;
		Refresh();
	}

	public void Refresh()
	{
		var fresh = new List<OutlineRow>();
		if (_document is not null)
		{
			var problems = _diagnostics
				.Where(d => d.Level != DiagnosticLevel.Info)
				.GroupBy(d => d.Node)
				.ToDictionary(g => g.Key, g => string.Join("\n", g.Select(d => d.Message)));
			AddRows(_document.Root, 0, fresh, problems);
		}

		_syncing = true;
		for (int i = 0; i < fresh.Count; i++)
		{
			if (i >= Rows.Count)
				Rows.Add(fresh[i]);
			else if (!Rows[i].SameAs(fresh[i]))
				Rows[i] = fresh[i];
		}
		while (Rows.Count > fresh.Count)
			Rows.RemoveAt(Rows.Count - 1);
		_syncing = false;
		SyncSelection();
	}

	private static void AddRows(DesignNode node, int depth, List<OutlineRow> rows, Dictionary<DesignNode, string> problems)
	{
		rows.Add(new OutlineRow(node, depth, DetailOf(node), problems.GetValueOrDefault(node)));
		if (!node.IsExpanded)
			return;
		foreach (var child in node.Children)
			AddRows(child, depth + 1, rows, problems);
	}

	private static string DetailOf(DesignNode node)
	{
		if (node.Name is { } name)
			return name;
		foreach (string attribute in new[] { "Content", "Text", "Header", "Title", "x:Key", "Selector" })
		{
			if (node.Get(attribute) is not { Length: > 0 } value)
				continue;
			if (XamlMarkup.Parse(value) is { IsBinding: true } binding)
				value = "{" + binding.BindingPath + "}";
			return "\"" + (value.Length > 28 ? value[..27] + "…" : value) + "\"";
		}
		if (node.Text is { Length: > 0 } text)
			return "\"" + (text.Length > 28 ? text[..27] + "…" : text) + "\"";
		return "";
	}

	public void SyncSelection()
	{
		var selected = _document?.Selected;
		if (selected is not null && Rows.All(r => r.Node != selected))
		{
			bool opened = false;
			foreach (var ancestor in selected.Ancestors())
			{
				if (!ancestor.IsExpanded)
				{
					ancestor.IsExpanded = true;
					opened = true;
				}
			}
			if (opened)
			{
				Refresh();
				return;
			}
		}
		_syncing = true;
		SelectedRow = selected is null ? null : Rows.FirstOrDefault(r => r.Node == selected);
		_syncing = false;
	}

	partial void OnSelectedRowChanged(OutlineRow? value)
	{
		if (!_syncing && value is not null)
			_document?.Select(value.Node);
	}

	[RelayCommand]
	private void Toggle(OutlineRow? row)
	{
		if (row is null || !row.HasChildren)
			return;
		row.Node.IsExpanded = !row.Node.IsExpanded;
		if (!row.Node.IsExpanded && _document?.Selected is { } selected && selected.IsDescendantOf(row.Node))
			_document.Select(row.Node);
		Refresh();
	}

	public bool CanDrop(DesignNode source, DesignNode target, DropPlace place)
	{
		if (_document is null || source == target || source.Parent is null || target.IsDescendantOf(source) || source.IsPropertyElement)
			return false;
		if (place == DropPlace.Inside)
			return DesignRules.CanAddChild(_document, target, source, moving: source);
		return target.Parent is { } parent && !target.IsPropertyElement && DesignRules.CanAddChild(_document, parent, source, moving: source);
	}

	public void Drop(DesignNode source, DesignNode target, DropPlace place)
	{
		if (_document is null || !CanDrop(source, target, place))
			return;
		if (place == DropPlace.Inside)
		{
			_document.MoveNode(source, target, null);
			return;
		}
		var parent = target.Parent!;
		var siblings = parent.ContentChildren.Where(c => c != source).ToList();
		int index = siblings.IndexOf(target) + (place == DropPlace.After ? 1 : 0);
		_document.MoveNode(source, parent, index);
	}
}
