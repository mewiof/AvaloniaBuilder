using System.Collections.ObjectModel;
using Avalonia.Controls;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaBuilder.ViewModels;

public partial class PropertyGroupViewModel : ObservableObject
{
	private static readonly Dictionary<string, bool> Expanded = new(StringComparer.Ordinal)
	{
		[PropertyGridViewModel.DocumentGroup] = true,
		[PropertyClassifier.Common] = true,
		[PropertyClassifier.Layout] = true,
		[PropertyClassifier.Appearance] = true,
		[PropertyClassifier.TextGroup] = false,
		[PropertyClassifier.Behavior] = false,
		[PropertyGridViewModel.EventsGroup] = false,
		[PropertyClassifier.Other] = false,
		[PropertyGridViewModel.AttributesGroup] = true,
	};

	public PropertyGroupViewModel(string name, IEnumerable<PropertyItemViewModel> items)
	{
		Name = name;
		Items = new ObservableCollection<PropertyItemViewModel>(items);
		IsExpanded = Expanded.GetValueOrDefault(name, true);
	}

	public string Name { get; }

	public ObservableCollection<PropertyItemViewModel> Items { get; }

	[ObservableProperty]
	public partial bool IsExpanded { get; set; }

	[ObservableProperty]
	public partial bool IsVisible { get; set; } = true;

	partial void OnIsExpandedChanged(bool value) => Expanded[Name] = value;
}

public partial class PropertyGridViewModel : ObservableObject
{
	public const string DocumentGroup = "Document";
	public const string EventsGroup = "Events";
	public const string AttributesGroup = "Other attributes";

	private static readonly string[] CommonEvents =
	{
		"Click", "TextChanged", "SelectionChanged", "ValueChanged", "IsCheckedChanged", "Loaded", "Unloaded",
		"KeyDown", "KeyUp", "PointerPressed", "PointerReleased", "PointerMoved", "PointerEntered", "PointerExited",
		"Tapped", "DoubleTapped", "GotFocus", "LostFocus", "SizeChanged", "Opened", "Closing", "Closed",
	};

	private readonly HashSet<string> _rowNames = new(StringComparer.Ordinal);
	private DesignDocument? _document;
	private DesignNode? _node;
	private string _shape = "";
	private bool _syncing;

	public ObservableCollection<PropertyGroupViewModel> Groups { get; } = new();

	public Func<DesignNode, PropertyMeta, object?>? LiveValueProvider { get; set; }

	[ObservableProperty]
	public partial bool HasTarget { get; set; }

	[ObservableProperty]
	public partial string Header { get; set; } = "";

	[ObservableProperty]
	public partial string SubHeader { get; set; } = "";

	[ObservableProperty]
	public partial string ElementType { get; set; } = "";

	[ObservableProperty]
	public partial string? ElementTypeError { get; set; }

	[ObservableProperty]
	public partial string ElementName { get; set; } = "";

	[ObservableProperty]
	public partial string? ElementNameError { get; set; }

	[ObservableProperty]
	public partial bool CanHaveName { get; set; }

	[ObservableProperty]
	public partial string Filter { get; set; } = "";

	[ObservableProperty]
	public partial string NewAttributeName { get; set; } = "";

	[ObservableProperty]
	public partial string NewAttributeValue { get; set; } = "";

	public void SetTarget(DesignDocument? document, DesignNode? node)
	{
		var type = document is null || node is null ? null : XamlTypes.Resolve(document, node);
		var parent = node?.ObjectParent;
		var parentType = document is null || parent is null ? null : XamlTypes.Resolve(document, parent);

		bool same = ReferenceEquals(document, _document) && ReferenceEquals(node, _node);
		if (same && Shape(node, type, parentType) == _shape)
		{
			Refresh();
			return;
		}

		_document = document;
		_node = node;
		Rebuild(type, parentType);
		_shape = Shape(node, type, parentType);
	}

	private string Shape(DesignNode? node, Type? type, Type? parentType)
	{
		if (node is null)
			return "";
		string extras = string.Join(",", node.Attributes.Select(a => a.Name).Where(name => !_rowNames.Contains(name)));
		return $"{node.ElementName}|{type?.FullName}|{parentType?.FullName}|{node.Parent is null}|{node.Text is not null}|{extras}";
	}

	public void Refresh()
	{
		if (_node is null)
			return;
		_syncing = true;
		ElementName = _node.Name ?? "";
		ElementNameError = null;
		ElementType = _node.ElementName;
		ElementTypeError = null;
		_syncing = false;
		foreach (var group in Groups)
			foreach (var item in group.Items)
				item.Sync();
	}

	private void Rebuild(Type? type, Type? parentType)
	{
		Groups.Clear();
		_rowNames.Clear();
		var document = _document;
		var node = _node;
		HasTarget = node is not null && document is not null;
		if (document is null || node is null)
		{
			Header = "";
			SubHeader = "";
			return;
		}

		_syncing = true;
		Header = node.ElementName;
		CanHaveName = !node.IsPropertyElement;
		ElementName = node.Name ?? "";
		ElementNameError = null;
		ElementType = node.ElementName;
		ElementTypeError = null;
		_syncing = false;

		var parent = node.ObjectParent;
		SubHeader = node.IsPropertyElement ? "a property written as an element"
			: type is null ? "a type that the designer does not know"
			: parent is null ? "the root of the document"
			: "in " + parent.ElementName;

		var covered = new HashSet<string>(StringComparer.Ordinal) { "x:Name", "Name" };
		var byGroup = new Dictionary<string, List<(int Rank, PropertyItemViewModel Item)>>(StringComparer.Ordinal);
		void Add(string group, int rank, PropertyItemViewModel item)
		{
			if (!covered.Add(item.Name))
				return;
			if (!byGroup.TryGetValue(group, out var list))
				byGroup[group] = list = new List<(int, PropertyItemViewModel)>();
			list.Add((rank, item));
		}

		if (node == document.Root)
		{
			Add(DocumentGroup, 0, new PropertyItemViewModel("x:Class", ItemKind.Raw, null,
				() => document.ViewClass,
				(value, _) => document.SetViewClass(value),
				typeHint: "The full name of the view class. Example: MyApp.Views.MainWindow",
				validate: CheckClassName));
			Add(DocumentGroup, 1, new PropertyItemViewModel("View model", ItemKind.Raw, null,
				() => document.ViewModelClass,
				(value, _) => document.SetViewModelClass(value),
				typeHint: "The full name of the view model class (x:DataType). Example: MyApp.ViewModels.MainViewModel",
				validate: CheckClassName));
			covered.Add("x:DataType");
		}

		if (node.Text is not null)
		{
			Add(PropertyClassifier.Common, -1, new PropertyItemViewModel("(text inside)", ItemKind.Raw, null,
				() => node.Text,
				(value, _) => document.SetText(node, value),
				typeHint: "The text between the start tag and the end tag."));
		}

		var metaType = node.IsPropertyElement ? null : type ?? typeof(Control);
		if (metaType is not null)
		{
			var meta = XamlTypes.GetMeta(metaType);

			foreach (var property in meta.Properties)
			{
				if (PropertyClassifier.IsHidden(property.Name))
					continue;
				var kind = ToItemKind(PropertyClassifier.GetKind(property.ValueType));
				bool isSet = node.Has(property.Name);
				if (!isSet && property.ClrProperty?.IsDefined(typeof(ObsoleteAttribute), inherit: true) == true)
					continue;
				if ((kind is null || !property.CanWrite) && !isSet)
					continue;
				string category = PropertyClassifier.GetCategory(property);
				Add(category, PropertyClassifier.GetRank(category, property.Name), MakeItem(document, node, property.Name, kind ?? ItemKind.Raw, property));
			}

			if (parentType is not null && typeof(Panel).IsAssignableFrom(parentType))
			{
				int rank = -100;
				foreach (var attached in XamlTypes.GetAttached(parentType, typeof(Control)).OrderBy(a => AttachedRank(a.Name)))
				{
					var kind = ToItemKind(PropertyClassifier.GetKind(attached.ValueType));
					if (kind is null || attached.Name is "IsSharedSizeScope")
						continue;
					Add(PropertyClassifier.Layout, rank++, MakeItem(document, node, attached.XamlName, kind.Value, attached));
				}
			}

			var tip = XamlTypes.GetAttached(typeof(ToolTip), typeof(Control)).FirstOrDefault(p => p.Name == "Tip");
			if (tip is not null && type is not null)
				Add(PropertyClassifier.Behavior, 50, MakeItem(document, node, "ToolTip.Tip", ItemKind.Text, tip));

			foreach (var info in meta.Events)
			{
				int index = Array.IndexOf(CommonEvents, info.Name);
				bool own = !IsBaseType(info.DeclaringType);
				if (index < 0 && !own && !node.Has(info.Name))
					continue;
				int rank = index >= 0 ? index : own ? 100 : 500;
				Add(EventsGroup, rank, new PropertyItemViewModel(info.Name, ItemKind.Event, null,
					() => node.Get(info.Name),
					(value, typing) => document.SetAttribute(node, info.Name, value, typing),
					typeHint: $"{info.Name}  (event with {info.ArgsType.Name}). Type the name of a method. The code-behind tab shows it."));
			}
		}

		_rowNames.UnionWith(covered);
		int order = 0;
		foreach (var attribute in node.Attributes)
		{
			string name = attribute.Name;
			Add(AttributesGroup, order++, new PropertyItemViewModel(name, ItemKind.Raw, null,
				() => node.Get(name),
				(value, typing) => document.SetAttribute(node, name, value, typing)));
		}

		string[] groupOrder =
		{
			DocumentGroup, PropertyClassifier.Common, PropertyClassifier.Layout, PropertyClassifier.Appearance,
			PropertyClassifier.TextGroup, PropertyClassifier.Behavior, EventsGroup, PropertyClassifier.Other, AttributesGroup,
		};
		foreach (string name in groupOrder)
		{
			if (!byGroup.TryGetValue(name, out var list))
				continue;
			var items = list.OrderBy(x => x.Rank).ThenBy(x => x.Item.Name, StringComparer.Ordinal).Select(x => x.Item);
			Groups.Add(new PropertyGroupViewModel(name, items));
		}
		ApplyFilter();
	}

	private static string? CheckClassName(string value) =>
		CodeNames.IsQualifiedName(value) ? null : "Give a full class name with dots, for example MyApp.Views.MainWindow.";

	private PropertyItemViewModel MakeItem(DesignDocument document, DesignNode node, string name, ItemKind kind, PropertyMeta meta) =>
		new(name, kind, meta,
			() => node.Get(name),
			(value, typing) => document.SetAttribute(node, name, value, typing),
			() => LiveValueProvider?.Invoke(node, meta));

	private static ItemKind? ToItemKind(EditorKind kind) => kind switch
	{
		EditorKind.Text => ItemKind.Text,
		EditorKind.Number => ItemKind.Number,
		EditorKind.Bool => ItemKind.Bool,
		EditorKind.Enum => ItemKind.Enum,
		EditorKind.Brush => ItemKind.Brush,
		EditorKind.Command => ItemKind.BindingOnly,
		EditorKind.BindingOnly => ItemKind.BindingOnly,
		_ => null,
	};

	private static int AttachedRank(string name) => name switch
	{
		"Row" or "Left" or "Dock" => 0,
		"Column" or "Top" => 1,
		"RowSpan" or "Right" => 2,
		"ColumnSpan" or "Bottom" => 3,
		_ => 10,
	};

	private static bool IsBaseType(Type type) =>
		type == typeof(Avalonia.Interactivity.Interactive) || type == typeof(Avalonia.Input.InputElement) || type == typeof(Control)
		|| type == typeof(Avalonia.Layout.Layoutable) || type == typeof(Avalonia.Visual) || type == typeof(Avalonia.StyledElement)
		|| type == typeof(Avalonia.AvaloniaObject) || type == typeof(Avalonia.Animation.Animatable)
		|| type == typeof(Avalonia.Controls.Primitives.TemplatedControl) || type == typeof(TopLevel) || type == typeof(WindowBase);

	partial void OnElementTypeChanged(string value)
	{
		if (_syncing || _document is null || _node is null)
			return;
		string text = value.Trim();
		if (text == _node.ElementName)
		{
			ElementTypeError = null;
			return;
		}

		int colon = text.IndexOf(':');
		string prefix = colon >= 0 ? text[..colon] : "";
		string name = colon >= 0 ? text[(colon + 1)..] : text;
		bool nameIsValid = _node.IsPropertyElement
			? name.Contains('.') && name.Split('.').All(CodeNames.IsIdentifier)
			: CodeNames.IsIdentifier(name);
		if (!nameIsValid || (colon >= 0 && !CodeNames.IsIdentifier(prefix)))
		{
			ElementTypeError = "Give a type name. Examples: Button, DockPanel, local:MyControl.";
			return;
		}

		ElementTypeError = null;
		var node = _node;
		var document = _document;
		document.Edit(() =>
		{
			if (prefix.Length > 0)
				Designer.DesignActions.MergeNamespaces(document, new[] { new XamlNamespace(prefix, "using:" + prefix) });
			node.Prefix = prefix;
			node.TypeName = name;
		});
	}

	partial void OnElementNameChanged(string value)
	{
		if (_syncing || _document is null || _node is null)
			return;
		string name = value.Trim();
		if (name.Length == 0)
		{
			ElementNameError = null;
			_document.Edit(() =>
			{
				_node.Remove("x:Name");
				_node.Remove("Name");
			}, ChangeKind.Attributes);
			return;
		}
		if (!CodeNames.IsIdentifier(name))
		{
			ElementNameError = "A name has letters, digits and _ only, and it does not start with a digit.";
			return;
		}
		if (_document.Root.SelfAndDescendants().Any(n => n != _node && n.Name == name))
		{
			ElementNameError = "Another element has this name already.";
			return;
		}
		ElementNameError = null;
		var node = _node;
		_document.Edit(() =>
		{
			node.Remove("Name");
			node.Set("x:Name", name);
		}, ChangeKind.Attributes, "x:Name:" + node.GetHashCode());
	}

	partial void OnFilterChanged(string value) => ApplyFilter();

	private void ApplyFilter()
	{
		string filter = Filter.Trim();
		foreach (var group in Groups)
		{
			bool any = false;
			foreach (var item in group.Items)
			{
				item.IsVisible = filter.Length == 0 || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
				any |= item.IsVisible;
			}
			group.IsVisible = any;
		}
	}

	[RelayCommand]
	private void AddAttribute()
	{
		string name = NewAttributeName.Trim();
		if (_document is null || _node is null || name.Length == 0 || name.Any(char.IsWhiteSpace))
			return;
		_document.SetAttribute(_node, name, NewAttributeValue);
		NewAttributeName = "";
		NewAttributeValue = "";
	}
}
