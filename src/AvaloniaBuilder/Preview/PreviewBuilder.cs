using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Preview;

public sealed class PreviewBuilder
{
	private static readonly Dictionary<string, (object? Value, string? Error)> LoaderCache = new(StringComparer.Ordinal);

	private static readonly IBrush PlaceholderStroke = new ImmutableSolidColorBrush(Color.FromArgb(200, 128, 128, 128));
	private static readonly IBrush PlaceholderFill = new ImmutableSolidColorBrush(Color.FromArgb(36, 128, 128, 128));

	private readonly DesignDocument _document;
	private readonly PreviewOptions _options;
	private readonly Dictionary<DesignNode, object> _objects = new();
	private readonly List<PreviewDiagnostic> _diagnostics = new();
	private readonly HashSet<DesignNode> _emptyContainers = new();
	private readonly List<Action> _cleanup = new();
	private readonly HashSet<object> _loaderObjects = new(ReferenceEqualityComparer.Instance);

	private Type? _realRootType;

	private PreviewBuilder(DesignDocument document, PreviewOptions options)
	{
		_document = document;
		_options = options;
	}

	public static PreviewResult Build(DesignDocument document, PreviewOptions? options = null)
	{
		var builder = new PreviewBuilder(document, options ?? new PreviewOptions());
		Control? root = null;
		try
		{
			root = builder.BuildRoot();
		}
		catch (Exception ex)
		{
			builder.Report(DiagnosticLevel.Error, document.Root, null, Unwrap(ex).Message);
		}
		return new PreviewResult(root, builder._objects, builder._diagnostics, builder._emptyContainers, builder._cleanup);
	}

	private Control? BuildRoot()
	{
		var rootNode = _document.Root;
		var type = XamlTypes.Resolve(_document, rootNode);
		if (type is null)
		{
			Report(DiagnosticLevel.Error, rootNode, null, $"The root element <{rootNode.ElementName}> is not a known Avalonia element.");
			return null;
		}
		if (!typeof(Control).IsAssignableFrom(type))
		{
			Report(DiagnosticLevel.Info, rootNode, null,
				$"<{rootNode.ElementName}> is not a control, so there is nothing to draw. You can still edit it in the outline and in the XAML tab.");
			return null;
		}
		return BuildObject(rootNode, isRoot: true, wantControl: true) as Control;
	}

	private object? BuildObject(DesignNode node, bool isRoot, bool wantControl)
	{
		var type = XamlTypes.Resolve(_document, node);
		if (type is null)
		{
			if (node.Prefix.Length == 0)
				Report(DiagnosticLevel.Error, node, null, $"<{node.ElementName}> is not a known Avalonia element.");
			return wantControl ? Remember(node, MakePlaceholder(node)) : null;
		}

		if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal)
			|| type == typeof(TimeSpan) || type == typeof(Uri))
		{
			if (ValueConverter.TryConvert(node.Text ?? "", type, _options.ValueContext, out var simple, out var simpleError))
				return simple;
			Report(DiagnosticLevel.Error, node, null, simpleError!);
			return null;
		}

		var createType = type;
		if (typeof(TopLevel).IsAssignableFrom(type))
		{
			if (!isRoot)
			{
				Report(DiagnosticLevel.Error, node, null, "A window cannot be inside another element.");
				return wantControl ? Remember(node, MakePlaceholder(node)) : null;
			}
			_realRootType = type;
			createType = typeof(UserControl);
		}

		if (NeedsLoader(type))
			return BuildWithLoader(node);

		var meta = XamlTypes.GetMeta(createType);
		if (!meta.CanCreate)
		{
			Report(DiagnosticLevel.Error, node, null, $"<{node.ElementName}> cannot be created. The class is abstract or has no public constructor.");
			return wantControl ? Remember(node, MakePlaceholder(node)) : null;
		}

		object instance;
		try
		{
			instance = Activator.CreateInstance(createType)!;
		}
		catch (Exception ex)
		{
			Report(DiagnosticLevel.Error, node, null, $"<{node.ElementName}> could not be created. {Unwrap(ex).Message}");
			return wantControl ? Remember(node, MakePlaceholder(node)) : null;
		}

		Remember(node, instance);
		var initialize = instance as System.ComponentModel.ISupportInitialize;
		initialize?.BeginInit();
		try
		{
			ApplyAttributes(node, instance, meta);
			ApplyChildren(node, instance, meta);
		}
		finally
		{
			initialize?.EndInit();
		}
		if (_options.DesignMode)
			ApplyDesignTweaks(node, instance, type, isRoot);
		return instance;
	}

	private T Remember<T>(DesignNode node, T instance) where T : notnull
	{
		_objects[node] = instance;
		return instance;
	}

	private void ApplyAttributes(DesignNode node, object instance, TypeMeta meta)
	{
		foreach (var attribute in node.Attributes)
			if (!attribute.Name.StartsWith("d:", StringComparison.Ordinal))
				ApplyAttribute(node, instance, meta, attribute.Name, attribute.Name, attribute.Value);
		foreach (var attribute in node.Attributes)
		{
			if (!attribute.Name.StartsWith("d:", StringComparison.Ordinal))
				continue;
			string local = attribute.Name[2..];
			if (local is "DesignWidth" or "DesignHeight" or "DataContext")
				continue;
			ApplyAttribute(node, instance, meta, attribute.Name, local, attribute.Value);
		}
	}

	private void ApplyAttribute(DesignNode node, object instance, TypeMeta meta, string attributeName, string name, string value)
	{
		try
		{
			int colon = name.IndexOf(':');
			if (colon > 0)
			{
				if (name == "x:Name" && instance is StyledElement named)
					named.Name = value;
				return;
			}

			if (name == "Name")
			{
				if (instance is StyledElement named)
					named.Name = value;
				return;
			}
			if (name == "Classes")
			{
				if (instance is StyledElement styled && !XamlMarkup.IsMarkup(value))
					foreach (string part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
						styled.Classes.Add(part);
				return;
			}
			if (name.StartsWith("Classes.", StringComparison.Ordinal))
			{
				if (instance is StyledElement styled && bool.TryParse(value, out bool on) && on)
					styled.Classes.Add(name["Classes.".Length..]);
				return;
			}

			var property = FindProperty(node, instance, meta, attributeName, name);
			if (property is not null)
				SetFromText(node, instance, property, attributeName, value);
		}
		catch (Exception ex)
		{
			Report(DiagnosticLevel.Error, node, attributeName, $"{attributeName}: {Unwrap(ex).Message}");
		}
	}

	private PropertyMeta? FindProperty(DesignNode node, object instance, TypeMeta meta, string attributeName, string name)
	{
		var instanceType = instance.GetType();
		int dot = name.IndexOf('.');
		if (dot > 0)
		{
			string ownerName = name[..dot];
			string propertyName = name[(dot + 1)..];
			var owner = XamlTypes.ResolveAvalonia(ownerName);
			if (owner is null)
			{
				Report(DiagnosticLevel.Warning, node, attributeName, $"{attributeName}: the type {ownerName} is not known.");
				return null;
			}
			if (owner.IsAssignableFrom(instanceType) && meta.FindProperty(propertyName) is { } own)
				return own;
			var attached = XamlTypes.FindAttached(owner, propertyName, instanceType);
			if (attached is not null)
				return new PropertyMeta(propertyName, attached.PropertyType, owner, attached, null, true, isAttached: true);
			if (owner.GetField(propertyName + "Event", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy) is not null)
				return null;
			Report(DiagnosticLevel.Error, node, attributeName, $"{ownerName} has no property '{propertyName}'.");
			return null;
		}

		if (meta.FindProperty(name) is { } property)
			return property;
		if (meta.FindEvent(name) is not null)
			return null;

		if (_realRootType is not null && instance is UserControl && ReferenceEquals(_objects.GetValueOrDefault(_document.Root), instance))
		{
			var real = XamlTypes.GetMeta(_realRootType);
			if (real.FindProperty(name) is not null || real.FindEvent(name) is not null)
				return null;
			Report(DiagnosticLevel.Error, node, attributeName, $"{_realRootType.Name} has no property '{name}'.");
			return null;
		}

		Report(DiagnosticLevel.Error, node, attributeName, $"{instanceType.Name} has no property '{name}'.");
		return null;
	}

	private void SetFromText(DesignNode node, object instance, PropertyMeta property, string attributeName, string value)
	{
		if (XamlMarkup.IsMarkup(value))
		{
			var markup = XamlMarkup.Parse(value);
			if (markup is null)
			{
				Report(DiagnosticLevel.Error, node, attributeName, $"{attributeName}: the curly braces do not match.");
				return;
			}
			ApplyMarkup(instance, property, markup);
			return;
		}

		if (!property.CanWrite)
		{
			Report(DiagnosticLevel.Error, node, attributeName, $"{attributeName} is read-only.");
			return;
		}

		string text = XamlMarkup.Unescape(value);
		if (!ValueConverter.TryConvert(text, property.ValueType, _options.ValueContext, out var converted, out var error))
		{
			var level = error!.Contains("image file", StringComparison.Ordinal) ? DiagnosticLevel.Info : DiagnosticLevel.Error;
			Report(level, node, attributeName, $"{attributeName}: {error}");
			return;
		}
		property.SetValue(instance, converted);
	}

	private void ApplyMarkup(object instance, PropertyMeta property, XamlMarkup markup)
	{
		if (markup.IsBinding)
		{
			ApplyBindingPlaceholder(instance, property, markup);
		}
		else if (markup.IsResource)
		{
			if (instance is StyledElement element && property.AvaloniaProperty is { IsReadOnly: false } target && markup.ResourceKey is { } key)
			{
				var targetType = property.ValueType;
				element.Bind(target, element.GetResourceObservable(key, found => ConvertResource(found, targetType)));
			}
		}
		else if (markup.Name == "x:Null")
		{
			if (property.CanWrite)
				property.SetValue(instance, null);
		}
		else if (markup.Name is "x:True" or "x:False")
		{
			if (property.CanWrite)
				property.SetValue(instance, markup.Name == "x:True");
		}
	}

	private static object? ConvertResource(object? found, Type targetType)
	{
		if (found is Color color && targetType.IsAssignableFrom(typeof(ImmutableSolidColorBrush)))
			return new ImmutableSolidColorBrush(color);
		return found;
	}

	private static void ApplyBindingPlaceholder(object instance, PropertyMeta property, XamlMarkup markup)
	{
		if (!property.CanWrite)
			return;
		var type = Nullable.GetUnderlyingType(property.ValueType) ?? property.ValueType;
		string path = markup.BindingPath ?? "";

		if (property.Name == "ItemsSource")
		{
			property.SetValue(instance, new[] { "Item 1", "Item 2", "Item 3" });
			return;
		}
		if (type != typeof(string) && type != typeof(object))
			return;
		if (property.Name is "DataContext" or "Tag" or "CommandParameter" or "SelectedItem" or "SelectedValue" or "Icon")
			return;
		property.SetValue(instance, path.Length == 0 ? "{Binding}" : "{" + path + "}");
	}

	private void ApplyChildren(DesignNode node, object instance, TypeMeta meta)
	{
		var content = new List<DesignNode>();
		foreach (var child in node.Children)
		{
			if (child.IsPropertyElement)
				ApplyPropertyElement(node, instance, child);
			else
				content.Add(child);
		}

		if (instance is IResourceDictionary dictionary)
		{
			AddKeyedChildren(dictionary, content);
			return;
		}

		var property = meta.ContentProperty;
		if (content.Count == 0)
		{
			if (!string.IsNullOrEmpty(node.Text))
				ApplyText(node, instance, meta, node.Text);
			return;
		}

		if (property is null)
		{
			foreach (var child in content)
			{
				var item = BuildObject(child, isRoot: false, wantControl: false);
				if (item is not null && !TryAdd(instance, item, child))
					Report(DiagnosticLevel.Error, child, null, $"<{node.ElementName}> cannot have child elements.");
			}
			return;
		}
		AssignChildren(node, instance, property, content);
	}

	private void ApplyText(DesignNode node, object instance, TypeMeta meta, string text)
	{
		var property = meta.ContentProperty;
		if (property is not null && property.ValueType != typeof(string) && property.ValueType != typeof(object)
			&& meta.FindProperty("Text") is { CanWrite: true } textProperty && textProperty.ValueType == typeof(string))
			property = textProperty;

		if (property is null)
		{
			Report(DiagnosticLevel.Error, node, null, $"<{node.ElementName}> cannot have text inside.");
			return;
		}
		try
		{
			if (property.ValueType == typeof(string) || property.ValueType == typeof(object))
				property.SetValue(instance, text);
			else if (SafeGet(property, instance) is IList list)
				list.Add(text);
			else if (ValueConverter.TryConvert(text, property.ValueType, _options.ValueContext, out var converted, out var error))
				property.SetValue(instance, converted);
			else
				Report(DiagnosticLevel.Error, node, null, error!);
		}
		catch (Exception ex)
		{
			Report(DiagnosticLevel.Error, node, null, Unwrap(ex).Message);
		}
	}

	private void AssignChildren(DesignNode ownerNode, object instance, PropertyMeta property, List<DesignNode> children)
	{
		object? current = SafeGet(property, instance);
		bool wantControl = typeof(Control).IsAssignableFrom(property.ValueType) || current is Controls;

		if (current is IResourceDictionary dictionary && !(children.Count == 1 && children[0].TypeName == "ResourceDictionary"))
		{
			AddKeyedChildren(dictionary, children);
			return;
		}

		bool isCollection = current is not null and not string && property.ValueType != typeof(object)
			&& (current is IList || HasAddMethod(current.GetType()));
		if (isCollection && children.Count == 1 && property.CanWrite
			&& XamlTypes.Resolve(_document, children[0]) is { } onlyType && property.ValueType.IsAssignableFrom(onlyType))
			isCollection = false;

		if (isCollection)
		{
			foreach (var child in children)
			{
				var item = BuildObject(child, isRoot: false, wantControl);
				if (item is not null)
					TryAdd(current!, item, child);
			}
			return;
		}

		if (children.Count > 1)
		{
			Report(DiagnosticLevel.Error, children[1], null,
				$"<{ownerNode.ElementName}> can have only one child. Put the children into a panel, for example a StackPanel or a Grid.");
		}
		if (!property.CanWrite)
		{
			Report(DiagnosticLevel.Error, children[0], null, $"{ownerNode.ElementName}: the property {property.Name} is read-only.");
			return;
		}
		bool acceptsControl = property.ValueType == typeof(object) || property.ValueType.IsAssignableFrom(typeof(Control))
			|| typeof(Control).IsAssignableFrom(property.ValueType);
		var value = BuildObject(children[0], isRoot: false, wantControl: acceptsControl);
		if (value is null)
			return;
		try
		{
			property.SetValue(instance, value);
		}
		catch (Exception ex)
		{
			Report(DiagnosticLevel.Error, children[0], null, Unwrap(ex).Message);
		}
	}

	private void ApplyPropertyElement(DesignNode ownerNode, object instance, DesignNode element)
	{
		try
		{
			string ownerName = element.OwnerTypeName;
			string propertyName = element.PropertyName;
			if (ownerName == "Design" || propertyName == "Transitions")
				return;

			var ownerType = XamlTypes.Resolve(_document, element.Prefix, ownerName);
			if (ownerType is null)
			{
				if (element.Prefix.Length == 0)
					Report(DiagnosticLevel.Warning, element, null, $"<{element.ElementName}>: the type {ownerName} is not known.");
				return;
			}

			var instanceType = instance.GetType();
			PropertyMeta? property = null;
			bool isRootStandIn = _realRootType is not null && ownerNode == _document.Root;
			if (ownerType.IsAssignableFrom(instanceType) || (isRootStandIn && ownerType.IsAssignableFrom(_realRootType)))
			{
				property = XamlTypes.GetMeta(instanceType).FindProperty(propertyName);
				if (property is null && isRootStandIn && XamlTypes.GetMeta(_realRootType!).FindProperty(propertyName) is not null)
					return;
			}
			else if (XamlTypes.FindAttached(ownerType, propertyName, instanceType) is { } attached)
			{
				property = new PropertyMeta(propertyName, attached.PropertyType, ownerType, attached, null, true, isAttached: true);
			}

			if (property is null)
			{
				Report(DiagnosticLevel.Error, element, null, $"{ownerName} has no property '{propertyName}'.");
				return;
			}

			var children = element.Children.Where(c => !c.IsPropertyElement).ToList();
			if (children.Count == 0)
			{
				if (!string.IsNullOrEmpty(element.Text))
					SetFromText(element, instance, property, element.ElementName, element.Text);
				return;
			}
			AssignChildren(ownerNode, instance, property, children);
		}
		catch (Exception ex)
		{
			Report(DiagnosticLevel.Error, element, null, Unwrap(ex).Message);
		}
	}

	private void AddKeyedChildren(IResourceDictionary dictionary, List<DesignNode> children)
	{
		foreach (var child in children)
		{
			string? keyText = child.Get("x:Key");
			object? key = keyText;
			if (XamlMarkup.Parse(keyText) is { Name: "x:Type", Positional.Count: > 0 } typeKey)
				key = XamlTypes.ResolveAvalonia(typeKey.Positional[0]);
			if (key is null)
			{
				if (child.TypeName != "ResourceDictionary")
					Report(DiagnosticLevel.Warning, child, null, $"<{child.ElementName}> in a resource dictionary needs an x:Key.");
				continue;
			}
			var value = BuildObject(child, isRoot: false, wantControl: false);
			if (value is null)
				continue;
			try
			{
				dictionary[key] = value;
			}
			catch (Exception ex)
			{
				Report(DiagnosticLevel.Error, child, null, Unwrap(ex).Message);
			}
		}
	}

	private static bool HasAddMethod(Type type) =>
		type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.Name == "Add" && m.GetParameters().Length == 1);

	private bool TryAdd(object collection, object item, DesignNode itemNode)
	{
		try
		{
			Action? remove;
			if (collection is IList list)
			{
				list.Add(item);
				remove = () => list.Remove(item);
			}
			else
			{
				var methods = collection.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
				bool Fits(MethodInfo m, string name) =>
					m.Name == name && m.GetParameters() is { Length: 1 } p && p[0].ParameterType.IsInstanceOfType(item);
				var add = methods.FirstOrDefault(m => Fits(m, "Add"));
				if (add is null)
					return false;
				add.Invoke(collection, new[] { item });
				var removeMethod = methods.FirstOrDefault(m => Fits(m, "Remove"));
				remove = removeMethod is null ? null : () => removeMethod.Invoke(collection, new[] { item });
			}

			if (remove is not null && _loaderObjects.Contains(item))
				_cleanup.Add(remove);
			return true;
		}
		catch (Exception ex)
		{
			Report(DiagnosticLevel.Error, itemNode, null, Unwrap(ex).Message);
			return true;
		}
	}

	private static object? SafeGet(PropertyMeta property, object instance)
	{
		try
		{
			return property.GetValue(instance);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static bool NeedsLoader(Type type) =>
		typeof(IStyle).IsAssignableFrom(type)
		|| typeof(IDataTemplate).IsAssignableFrom(type)
		|| type.Name.EndsWith("Template", StringComparison.Ordinal);

	private object? BuildWithLoader(DesignNode node)
	{
		var copy = node.Clone();
		copy.Remove("x:Key");
		foreach (var n in copy.SelfAndDescendants())
		{
			n.Remove("x:DataType");
			n.Remove("DataType");
			n.Remove("x:CompileBindings");
			n.Comments.Clear();
			n.TrailingComments.Clear();
		}

		if (!UsesOnlyKnownNamespaces(copy))
		{
			Report(DiagnosticLevel.Info, node, null, $"<{node.ElementName}> uses types of your app. It is not shown in the preview.");
			return null;
		}

		string xaml = DesignXamlWriter.WriteFragment(_document, new[] { copy }, withNamespaces: true);
		if (!LoaderCache.TryGetValue(xaml, out var entry) || (entry.Value is not null && !_loaderObjects.Add(entry.Value)))
		{
			entry = Load(xaml);
			if (LoaderCache.Count > 500)
				LoaderCache.Clear();
			LoaderCache.TryAdd(xaml, entry);
			if (entry.Value is not null)
				_loaderObjects.Add(entry.Value);
		}

		if (entry.Value is null)
		{
			Report(DiagnosticLevel.Info, node, null, $"<{node.ElementName}> is not shown in the preview. {entry.Error}");
			return null;
		}
		_objects[node] = entry.Value;
		return entry.Value;
	}

	private static (object? Value, string? Error) Load(string xaml)
	{
		try
		{
			return (AvaloniaRuntimeXamlLoader.Load(xaml, typeof(PreviewBuilder).Assembly), null);
		}
		catch (Exception ex)
		{
			return (null, Unwrap(ex).Message);
		}
	}

	private bool UsesOnlyKnownNamespaces(DesignNode fragment)
	{
		foreach (var node in fragment.SelfAndDescendants())
		{
			if (!IsKnownPrefix(node.Prefix))
				return false;
			foreach (var attribute in node.Attributes)
			{
				int colon = attribute.Name.IndexOf(':');
				if (colon > 0 && !IsKnownPrefix(attribute.Name[..colon]))
					return false;
				foreach (var ns in _document.Namespaces)
					if (ns.Prefix.Length > 0 && !IsKnownPrefix(ns.Prefix) && attribute.Value.Contains(ns.Prefix + ":", StringComparison.Ordinal))
						return false;
			}
		}
		return true;
	}

	private bool IsKnownPrefix(string prefix)
	{
		if (prefix.Length == 0 || prefix == "xml")
			return true;
		string? uri = _document.GetNamespaceUri(prefix);
		if (uri is XamlNs.Avalonia or XamlNs.Xaml or XamlNs.Design or XamlNs.Compatibility)
			return true;
		return uri is not null && XamlNs.ClrNamespaceOf(uri) == "System";
	}

	private void ApplyDesignTweaks(DesignNode node, object instance, Type type, bool isRoot)
	{
		var selected = _options.Selected;
		bool selectionInside = selected is not null && selected.IsDescendantOf(node);

		if (instance is TabControl tabControl)
		{
			var tabs = node.ContentChildren.ToList();
			int index = -1;
			if (selected is not null)
				index = tabs.FindIndex(tab => tab == selected || selected.IsDescendantOf(tab));
			if (index >= 0)
				_options.ActiveTabs[node] = index;
			else if (!_options.ActiveTabs.TryGetValue(node, out index))
				index = -1;
			if (index >= 0 && index < tabControl.ItemCount)
				tabControl.SelectedIndex = index;
		}
		else if (instance is Expander expander && selectionInside)
		{
			expander.IsExpanded = true;
		}
		else if (instance is TreeViewItem treeItem && selectionInside)
		{
			treeItem.IsExpanded = true;
		}

		if (instance is Control control && DesignRules.IsDropContainer(type) && IsEmpty(node, type))
		{
			_emptyContainers.Add(node);
			if (!isRoot && DesignRules.NeedsDesignSize(type))
			{
				if (double.IsNaN(control.Width) && control.MinWidth == 0)
					control.MinWidth = 64;
				if (double.IsNaN(control.Height) && control.MinHeight == 0)
					control.MinHeight = 32;
			}
		}
	}

	private static bool IsEmpty(DesignNode node, Type type)
	{
		if (node.ContentCount > 0 || !string.IsNullOrEmpty(node.Text))
			return false;
		string? contentName = XamlTypes.GetMeta(type).ContentProperty?.Name;
		if (contentName is not null && node.Has(contentName))
			return false;
		return !node.Has("ItemsSource") && !node.Has("Child") && !node.Has("Content");
	}

	private Control MakePlaceholder(DesignNode node)
	{
		var panel = new Panel { MinWidth = 48, MinHeight = 28 };
		panel.Children.Add(new Rectangle
		{
			Stroke = PlaceholderStroke,
			StrokeThickness = 1,
			StrokeDashArray = new AvaloniaList<double> { 4, 3 },
			Fill = PlaceholderFill,
		});
		panel.Children.Add(new TextBlock
		{
			Text = node.ElementName,
			FontSize = 12,
			Opacity = 0.8,
			Margin = new Thickness(8, 4),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
		});

		var meta = XamlTypes.GetMeta(typeof(Panel));
		foreach (var attribute in node.Attributes)
		{
			bool isLayout = attribute.Name is "Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight"
				or "Margin" or "HorizontalAlignment" or "VerticalAlignment" or "IsVisible" or "Opacity" or "ZIndex"
				|| DesignDocument.IsPlacementAttribute(attribute.Name);
			if (!isLayout || XamlMarkup.IsMarkup(attribute.Value))
				continue;
			var property = attribute.Name.Contains('.')
				? FindProperty(node, panel, meta, attribute.Name, attribute.Name)
				: meta.FindProperty(attribute.Name);
			if (property is not null)
				SetFromText(node, panel, property, attribute.Name, attribute.Value);
		}
		return panel;
	}

	private void Report(DiagnosticLevel level, DesignNode node, string? attribute, string message) =>
		_diagnostics.Add(new PreviewDiagnostic(level, node, attribute, message));

	private static Exception Unwrap(Exception ex)
	{
		while (ex is TargetInvocationException { InnerException: { } inner })
			ex = inner;
		return ex;
	}
}
