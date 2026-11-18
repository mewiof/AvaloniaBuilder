namespace AvaloniaBuilder.Model;

public sealed class DesignAttribute
{
	public DesignAttribute(string name, string value)
	{
		Name = name;
		Value = value;
	}

	public string Name { get; }

	public string Value { get; set; }

	public override string ToString() => $"{Name}=\"{Value}\"";
}

public sealed class DesignNode
{
	private readonly List<DesignNode> _children = new();
	private readonly List<DesignAttribute> _attributes = new();

	public DesignNode(string typeName, string prefix = "")
	{
		TypeName = typeName;
		Prefix = prefix;
	}

	public string Prefix { get; set; }

	public string TypeName { get; set; }

	public string ElementName => Prefix.Length == 0 ? TypeName : Prefix + ":" + TypeName;

	public bool IsPropertyElement => TypeName.Contains('.');

	public string PropertyName => TypeName[(TypeName.IndexOf('.') + 1)..];

	public string OwnerTypeName => IsPropertyElement ? TypeName[..TypeName.IndexOf('.')] : TypeName;

	public string? Text { get; set; }

	public List<string> Comments { get; } = new();

	public List<string> TrailingComments { get; } = new();

	public bool BlankLineBefore { get; set; }

	public bool IsExpanded { get; set; } = true;

	public int SourceLine { get; set; }

	public int SourceEndLine { get; set; }

	public DesignNode? Parent { get; private set; }

	public IReadOnlyList<DesignNode> Children => _children;

	public IReadOnlyList<DesignAttribute> Attributes => _attributes;

	public IEnumerable<DesignNode> ContentChildren => _children.Where(c => !c.IsPropertyElement);

	public int ContentCount
	{
		get
		{
			int count = 0;
			foreach (var child in _children)
				if (!child.IsPropertyElement)
					count++;
			return count;
		}
	}

	public string? Name => Get("x:Name") ?? Get("Name");

	public DesignNode Root
	{
		get
		{
			var node = this;
			while (node.Parent is not null)
				node = node.Parent;
			return node;
		}
	}

	public DesignNode? ObjectParent
	{
		get
		{
			var node = Parent;
			while (node is not null && node.IsPropertyElement)
				node = node.Parent;
			return node;
		}
	}

	public string? Get(string name)
	{
		foreach (var attribute in _attributes)
			if (attribute.Name == name)
				return attribute.Value;
		return null;
	}

	public bool Has(string name) => Get(name) is not null;

	public void Set(string name, string value)
	{
		foreach (var attribute in _attributes)
		{
			if (attribute.Name == name)
			{
				attribute.Value = value;
				return;
			}
		}

		int rank = AttributeOrder.Rank(name);
		int index = 0;
		for (int i = _attributes.Count - 1; i >= 0; i--)
		{
			if (AttributeOrder.Rank(_attributes[i].Name) <= rank)
			{
				index = i + 1;
				break;
			}
		}
		_attributes.Insert(index, new DesignAttribute(name, value));
	}

	public void AddAttributeRaw(string name, string value) => _attributes.Add(new DesignAttribute(name, value));

	public bool Remove(string name)
	{
		for (int i = 0; i < _attributes.Count; i++)
		{
			if (_attributes[i].Name == name)
			{
				_attributes.RemoveAt(i);
				return true;
			}
		}
		return false;
	}

	public void SetOrRemove(string name, string? value)
	{
		if (value is null)
			Remove(name);
		else
			Set(name, value);
	}

	public void Add(DesignNode child) => Insert(_children.Count, child);

	public void Insert(int index, DesignNode child)
	{
		if (child.Parent is not null)
			throw new InvalidOperationException("The node already has a parent. Remove it first.");
		if (child == this || IsDescendantOf(child))
			throw new InvalidOperationException("A node cannot contain itself.");
		index = Math.Clamp(index, 0, _children.Count);
		_children.Insert(index, child);
		child.Parent = this;
	}

	public bool RemoveChild(DesignNode child)
	{
		if (!_children.Remove(child))
			return false;
		child.Parent = null;
		return true;
	}

	public void ClearChildren()
	{
		foreach (var child in _children)
			child.Parent = null;
		_children.Clear();
	}

	public void Detach() => Parent?.RemoveChild(this);

	public int IndexOf(DesignNode child) => _children.IndexOf(child);

	public int Index => Parent?.IndexOf(this) ?? -1;

	public int ContentIndexOf(DesignNode child)
	{
		int index = 0;
		foreach (var c in _children)
		{
			if (c == child)
				return index;
			if (!c.IsPropertyElement)
				index++;
		}
		return -1;
	}

	public void InsertContent(int contentIndex, DesignNode child)
	{
		int seen = 0;
		int lastContent = -1;
		for (int i = 0; i < _children.Count; i++)
		{
			if (_children[i].IsPropertyElement)
				continue;
			if (seen == contentIndex)
			{
				Insert(i, child);
				return;
			}
			seen++;
			lastContent = i;
		}
		Insert(lastContent >= 0 ? lastContent + 1 : _children.Count, child);
	}

	public void AddContent(DesignNode child) => InsertContent(int.MaxValue, child);

	public bool IsDescendantOf(DesignNode other)
	{
		for (var node = Parent; node is not null; node = node.Parent)
			if (node == other)
				return true;
		return false;
	}

	public IEnumerable<DesignNode> Ancestors()
	{
		for (var node = Parent; node is not null; node = node.Parent)
			yield return node;
	}

	public IEnumerable<DesignNode> Descendants()
	{
		foreach (var child in _children)
		{
			yield return child;
			foreach (var descendant in child.Descendants())
				yield return descendant;
		}
	}

	public IEnumerable<DesignNode> SelfAndDescendants()
	{
		yield return this;
		foreach (var descendant in Descendants())
			yield return descendant;
	}

	public int[] GetPath()
	{
		var path = new List<int>();
		for (var node = this; node.Parent is not null; node = node.Parent)
			path.Add(node.Index);
		path.Reverse();
		return path.ToArray();
	}

	public DesignNode? FindByPath(int[] path)
	{
		var node = this;
		foreach (int index in path)
		{
			if (index < 0 || index >= node._children.Count)
				return null;
			node = node._children[index];
		}
		return node;
	}

	public DesignNode Clone()
	{
		var copy = new DesignNode(TypeName, Prefix)
		{
			Text = Text,
			BlankLineBefore = BlankLineBefore,
			IsExpanded = IsExpanded,
			SourceLine = SourceLine,
			SourceEndLine = SourceEndLine,
		};
		copy.Comments.AddRange(Comments);
		copy.TrailingComments.AddRange(TrailingComments);
		foreach (var attribute in _attributes)
			copy._attributes.Add(new DesignAttribute(attribute.Name, attribute.Value));
		foreach (var child in _children)
			copy.Add(child.Clone());
		return copy;
	}

	public override string ToString() => Name is { } name ? $"{ElementName} \"{name}\"" : ElementName;
}

public static class AttributeOrder
{
	private static readonly Dictionary<string, int> Ranks = Build();

	private static Dictionary<string, int> Build()
	{
		var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
		void Add(int rank, params string[] names)
		{
			foreach (string name in names)
				ranks[name] = rank;
		}

		Add(-50, "mc:Ignorable");
		Add(-30, "x:Class");
		Add(-29, "x:DataType");
		Add(-28, "x:CompileBindings");
		Add(0, "x:Name", "Name");
		Add(1, "x:Key");
		Add(2, "Classes");
		Add(20, "Title", "Header", "Content", "Text", "PlaceholderText", "Source", "Data", "Icon", "NavigateUri",
			"GroupName", "Mask", "Selector", "Property");
		Add(30, "IsChecked", "Value", "Minimum", "Maximum", "Increment", "SmallChange", "LargeChange", "TickFrequency",
			"IsSnapToTickEnabled", "SelectedIndex", "SelectedItem", "SelectedDate", "SelectedTime", "ItemsSource",
			"IsExpanded", "IsPaneOpen", "IsIndeterminate", "Orientation", "Spacing", "RowDefinitions",
			"ColumnDefinitions", "RowSpacing", "ColumnSpacing", "Rows", "Columns", "LastChildFill", "FormatString",
			"StartPoint", "EndPoint", "Points", "Stretch");
		Add(40, "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding",
			"HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment");
		Add(50, "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius", "BoxShadow", "Fill",
			"Stroke", "StrokeThickness", "Opacity", "FontFamily", "FontSize", "FontWeight", "FontStyle",
			"TextAlignment", "TextWrapping", "TextTrimming");
		Add(70, "IsEnabled", "IsVisible", "IsReadOnly", "IsHitTestVisible", "Focusable");
		Add(80, "Command", "CommandParameter", "HotKey");
		return ranks;
	}

	public static int Rank(string name)
	{
		if (Ranks.TryGetValue(name, out int rank))
			return rank;
		if (name.StartsWith("d:", StringComparison.Ordinal))
			return -40;
		if (name.StartsWith("Classes.", StringComparison.Ordinal))
			return 2;
		if (IsLayoutAttached(name))
			return 10;
		if (name.Contains('.'))
			return 85;
		return 60;
	}

	public static bool IsLayoutAttached(string name) =>
		name.StartsWith("Grid.", StringComparison.Ordinal)
		|| name.StartsWith("DockPanel.", StringComparison.Ordinal)
		|| name.StartsWith("Canvas.", StringComparison.Ordinal)
		|| name.StartsWith("RelativePanel.", StringComparison.Ordinal);
}
