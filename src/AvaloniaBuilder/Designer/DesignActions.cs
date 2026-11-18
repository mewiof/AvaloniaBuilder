using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Designer;

public static class DesignActions
{
	public static (DesignNode Parent, int? Index)? FindInsertionPoint(DesignDocument document, DesignNode newNode)
	{
		var start = document.Selected ?? document.Root;
		if (DesignRules.CanAddChild(document, start, newNode))
			return (start, null);

		for (var node = start; node.Parent is { } parent; node = parent)
		{
			if (!node.IsPropertyElement && DesignRules.CanAddChild(document, parent, newNode))
				return (parent, parent.ContentIndexOf(node) + 1);
		}

		foreach (var node in document.Root.SelfAndDescendants())
			if (DesignRules.CanAddChild(document, node, newNode))
				return (node, null);
		return null;
	}

	public static void Insert(DesignDocument document, DesignNode parent, int? index, IReadOnlyList<DesignNode> nodes,
		IEnumerable<XamlNamespace>? namespaces = null, IEnumerable<KeyValuePair<string, string?>>? attributes = null)
	{
		if (nodes.Count == 0)
			return;
		document.Edit(() =>
		{
			MergeNamespaces(document, namespaces);
			int? at = index;
			foreach (var node in nodes)
			{
				document.InsertNode(parent, node, at, select: false);
				if (at is not null)
					at++;
			}
			if (attributes is not null)
				foreach (var (name, value) in attributes)
					nodes[0].SetOrRemove(name, value);
			document.Select(nodes[0]);
		});
	}

	public static void MergeNamespaces(DesignDocument document, IEnumerable<XamlNamespace>? namespaces)
	{
		if (namespaces is null)
			return;
		foreach (var ns in namespaces)
		{
			if (document.GetNamespaceUri(ns.Prefix) is not null)
				continue;
			string uri = ns.Uri;
			if (uri == "using:" + ns.Prefix)
			{
				string root = ProjectExporter.GetRootNamespace(document);
				uri = ns.Prefix switch
				{
					"local" => "using:" + root,
					"vm" => "using:" + root + ".ViewModels",
					"views" => "using:" + root + ".Views",
					"controls" or "ctl" => "using:" + root + ".Controls",
					"conv" or "converters" => "using:" + root + ".Converters",
					"d" => XamlNs.Design,
					"mc" => XamlNs.Compatibility,
					_ => uri,
				};
			}
			document.EnsureNamespace(ns.Prefix, uri);
		}
	}

	public static string CopyText(DesignDocument document, DesignNode node)
	{
		var copy = node.Clone();
		copy.BlankLineBefore = false;
		return DesignXamlWriter.WriteFragment(document, new[] { copy }, withNamespaces: true);
	}

	public static string? Paste(DesignDocument document, string text)
	{
		List<DesignNode> nodes;
		List<XamlNamespace> namespaces;
		try
		{
			nodes = DesignXamlReader.ParseFragment(text, document, out namespaces);
		}
		catch (XamlReadException ex)
		{
			return "The clipboard does not hold valid XAML. " + ex.Message;
		}
		nodes = nodes.Where(n => !n.IsPropertyElement).ToList();
		if (nodes.Count == 0)
			return "The clipboard holds no XAML element.";

		if (nodes.Count == 1 && nodes[0].TypeName is "Window" or "UserControl" && nodes[0].ContentCount > 0)
		{
			var inner = nodes[0].ContentChildren.ToList();
			foreach (var child in inner)
				child.Detach();
			nodes = inner;
		}

		var place = FindInsertionPoint(document, nodes[0]);
		if (place is null)
			return $"There is no place for <{nodes[0].ElementName}> here. Select a panel first.";
		Insert(document, place.Value.Parent, place.Value.Index, nodes, namespaces);
		return null;
	}

	public static IReadOnlyList<string> WrapChoices { get; } = new[]
	{
		"Border", "Grid", "StackPanel", "DockPanel", "ScrollViewer", "Viewbox", "Expander", "GroupBox",
	};

	public static DesignNode MakeWrapper(string typeName)
	{
		var wrapper = new DesignNode(typeName);
		switch (typeName)
		{
			case "Border":
				wrapper.Set("BorderBrush", "Gray");
				wrapper.Set("BorderThickness", "1");
				wrapper.Set("CornerRadius", "4");
				wrapper.Set("Padding", "8");
				break;
			case "StackPanel":
				wrapper.Set("Spacing", "8");
				break;
			case "Expander":
				wrapper.Set("Header", "Expander");
				wrapper.Set("IsExpanded", "True");
				break;
			case "GroupBox":
				wrapper.Set("Header", "Group");
				break;
		}
		return wrapper;
	}

	public static bool CanWrap(DesignDocument document, DesignNode? node) =>
		node is { Parent: not null, IsPropertyElement: false };

	public static bool CanUnwrap(DesignDocument document, DesignNode? node)
	{
		if (node?.Parent is not { } parent || node.IsPropertyElement || node.ContentCount == 0)
			return false;
		if (node.Children.Any(c => c.IsPropertyElement))
			return false;
		var parentType = XamlTypes.Resolve(document, parent);
		if (parentType is null)
			return false;
		if (DesignRules.GetContentModel(parentType) == ContentModel.Single && node.ContentCount > 1)
			return false;
		return node.ContentChildren.All(child => DesignRules.CanAddChild(document, parent, child, moving: node)
			|| DesignRules.GetContentModel(parentType) == ContentModel.Many);
	}
}
