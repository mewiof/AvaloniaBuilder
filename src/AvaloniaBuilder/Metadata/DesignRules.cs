using System.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Metadata;

public enum ContentModel
{
	None,

	Text,

	Single,

	Many,
}

public static class DesignRules
{
	private static readonly Dictionary<string, string[]> AllowedChildren = new(StringComparer.Ordinal)
	{
		["TabControl"] = new[] { "TabItem" },
		["Menu"] = new[] { "MenuItem", "Separator" },
		["ContextMenu"] = new[] { "MenuItem", "Separator" },
		["MenuItem"] = new[] { "MenuItem", "Separator" },
		["ComboBox"] = new[] { "ComboBoxItem" },
		["ListBox"] = new[] { "ListBoxItem" },
		["TreeView"] = new[] { "TreeViewItem" },
		["TreeViewItem"] = new[] { "TreeViewItem" },
		["TabStrip"] = new[] { "TabStripItem" },
	};

	private static readonly Dictionary<string, string[]> AllowedParents = new(StringComparer.Ordinal)
	{
		["TabItem"] = new[] { "TabControl" },
		["MenuItem"] = new[] { "Menu", "MenuItem", "ContextMenu" },
		["ComboBoxItem"] = new[] { "ComboBox" },
		["ListBoxItem"] = new[] { "ListBox" },
		["TreeViewItem"] = new[] { "TreeView", "TreeViewItem" },
		["TabStripItem"] = new[] { "TabStrip" },
	};

	private static readonly HashSet<string> SingleChildContainers = new(StringComparer.Ordinal)
	{
		"Window", "UserControl", "ContentControl", "ScrollViewer", "Expander", "GroupBox", "HeaderedContentControl",
		"TabItem", "SplitView", "TransitioningContentControl", "RefreshContainer", "Border", "Viewbox",
		"LayoutTransformControl", "ThemeVariantScope", "Decorator", "ExperimentalAcrylicBorder", "ContentPage",
	};

	public static ContentModel GetContentModel(Type type)
	{
		var content = XamlTypes.GetMeta(type).ContentProperty;
		if (content is null)
			return ContentModel.None;
		var valueType = content.ValueType;
		if (valueType == typeof(string) || typeof(InlineCollection).IsAssignableFrom(valueType))
			return ContentModel.Text;
		if (valueType == typeof(object) || typeof(Control).IsAssignableFrom(valueType))
			return ContentModel.Single;
		if (typeof(IEnumerable).IsAssignableFrom(valueType))
			return ContentModel.Many;
		return ContentModel.None;
	}

	public static bool IsDropContainer(Type type)
	{
		if (typeof(Panel).IsAssignableFrom(type))
			return true;
		if (SingleChildContainers.Contains(type.Name))
			return true;
		if (AllowedChildren.ContainsKey(type.Name))
			return true;
		return type == typeof(ItemsControl) || type == typeof(Carousel);
	}

	public static bool CanAddChild(DesignDocument document, DesignNode parent, DesignNode child, DesignNode? moving = null)
	{
		if (child.IsPropertyElement)
			return false;

		if (parent.IsPropertyElement)
			return CanAddToPropertyElement(document, parent, moving);

		var parentType = XamlTypes.Resolve(document, parent);
		if (parentType is null)
			return false;

		if (AllowedChildren.TryGetValue(parentType.Name, out var allowed) && Array.IndexOf(allowed, child.TypeName) < 0)
			return false;
		if (child.Prefix.Length == 0 && AllowedParents.TryGetValue(child.TypeName, out var parents)
			&& Array.IndexOf(parents, parentType.Name) < 0)
			return false;

		switch (GetContentModel(parentType))
		{
			case ContentModel.Many:
				return true;
			case ContentModel.Single:
				var contentName = XamlTypes.GetMeta(parentType).ContentProperty?.Name;
				if (contentName is not null && parent.Has(contentName))
					return false;
				if (!string.IsNullOrEmpty(parent.Text))
					return false;
				return !parent.ContentChildren.Any(c => c != moving);
			default:
				return false;
		}
	}

	private static bool CanAddToPropertyElement(DesignDocument document, DesignNode propertyElement, DesignNode? moving)
	{
		var owner = propertyElement.Parent;
		if (owner is null)
			return false;
		var ownerType = XamlTypes.Resolve(document, owner);
		var property = ownerType is null ? null : XamlTypes.GetMeta(ownerType).FindProperty(propertyElement.PropertyName);
		if (property is null)
			return false;
		if (property.ValueType == typeof(object) || typeof(Control).IsAssignableFrom(property.ValueType))
			return !propertyElement.ContentChildren.Any(c => c != moving) && string.IsNullOrEmpty(propertyElement.Text);
		return typeof(Controls).IsAssignableFrom(property.ValueType) || property.ValueType == typeof(ItemCollection);
	}

	public static bool NeedsDesignSize(Type type)
	{
		if (typeof(Panel).IsAssignableFrom(type))
			return true;
		if (type == typeof(TabItem) || typeof(TopLevel).IsAssignableFrom(type))
			return false;
		if (SingleChildContainers.Contains(type.Name))
			return true;
		return type == typeof(ItemsControl) || type == typeof(ListBox) || type == typeof(TreeView) || type == typeof(TabControl);
	}

	public static bool IsWindow(Type? type) => type is not null && typeof(WindowBase).IsAssignableFrom(type);

	public static bool HidesContent(Type type) => type == typeof(TabControl) || type == typeof(Expander);
}
