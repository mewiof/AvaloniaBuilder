namespace AvaloniaBuilder.Model;

public sealed record XamlNamespace(string Prefix, string Uri);

public static class XamlNs
{
	public const string Avalonia = "https://github.com/avaloniaui";
	public const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
	public const string Design = "http://schemas.microsoft.com/expression/blend/2008";
	public const string Compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

	public static string? ClrNamespaceOf(string uri)
	{
		if (uri.StartsWith("using:", StringComparison.Ordinal))
			return uri["using:".Length..].Trim();
		if (uri.StartsWith("clr-namespace:", StringComparison.Ordinal))
		{
			string rest = uri["clr-namespace:".Length..];
			int semicolon = rest.IndexOf(';');
			return (semicolon >= 0 ? rest[..semicolon] : rest).Trim();
		}
		return null;
	}
}

public enum ChangeKind
{
	Attributes,

	Structure,

	Reset,
}

public sealed class DocumentChangedEventArgs : EventArgs
{
	public DocumentChangedEventArgs(ChangeKind kind) => Kind = kind;

	public ChangeKind Kind { get; }
}

public sealed class DesignDocument
{
	private sealed record Snapshot(DesignNode Root, List<XamlNamespace> Namespaces, int[]? Selection, int Version);

	private const int MaxUndo = 300;

	private readonly List<Snapshot> _undo = new();
	private readonly List<Snapshot> _redo = new();
	private int _editDepth;
	private ChangeKind _pendingKind;
	private string? _lastMergeKey;
	private long _lastEditTicks;
	private int _version;
	private int _savedVersion;
	private int _nextVersion = 1;

	public DesignDocument(DesignNode root, IEnumerable<XamlNamespace>? namespaces = null)
	{
		Root = root;
		Namespaces = namespaces?.ToList() ?? new List<XamlNamespace>();
		EnsureNamespace("", XamlNs.Avalonia);
		EnsureNamespace("x", XamlNs.Xaml);
	}

	public DesignNode Root { get; private set; }

	public List<XamlNamespace> Namespaces { get; private set; }

	public DesignNode? Selected { get; private set; }

	public string? FilePath { get; set; }

	public bool IsDirty => _version != _savedVersion;

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	public event EventHandler<DocumentChangedEventArgs>? Changed;

	public event EventHandler? SelectionChanged;

	public void MarkSaved() => _savedVersion = _version;

	public void Select(DesignNode? node)
	{
		if (node is not null && node.Root != Root)
			node = null;
		if (node == Selected)
			return;
		Selected = node;
		if (_editDepth > 0)
			return;
		_lastMergeKey = null;
		SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	public void Edit(Action change, ChangeKind kind = ChangeKind.Structure, string? mergeKey = null)
	{
		if (_editDepth > 0)
		{
			if (kind > _pendingKind)
				_pendingKind = kind;
			change();
			return;
		}

		long now = Environment.TickCount64;
		bool merge = mergeKey is not null && mergeKey == _lastMergeKey && _undo.Count > 0 && now - _lastEditTicks < 1500;
		if (!merge)
		{
			_undo.Add(TakeSnapshot());
			if (_undo.Count > MaxUndo)
				_undo.RemoveAt(0);
		}
		_redo.Clear();
		_lastMergeKey = mergeKey;
		_lastEditTicks = now;

		var selectedBefore = Selected;
		_pendingKind = kind;
		_editDepth++;
		try
		{
			change();
		}
		finally
		{
			_editDepth--;
		}

		_version = _nextVersion++;
		if (Selected is not null && Selected.Root != Root)
			Selected = null;
		Changed?.Invoke(this, new DocumentChangedEventArgs(_pendingKind));
		if (Selected != selectedBefore)
			SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	public void Undo()
	{
		if (_undo.Count == 0)
			return;
		_redo.Add(TakeSnapshot());
		Restore(Pop(_undo));
	}

	public void Redo()
	{
		if (_redo.Count == 0)
			return;
		_undo.Add(TakeSnapshot());
		Restore(Pop(_redo));
	}

	private static Snapshot Pop(List<Snapshot> stack)
	{
		var snapshot = stack[^1];
		stack.RemoveAt(stack.Count - 1);
		return snapshot;
	}

	private Snapshot TakeSnapshot() =>
		new(Root.Clone(), Namespaces.ToList(), Selected?.GetPath(), _version);

	private void Restore(Snapshot snapshot)
	{
		Root = snapshot.Root;
		Namespaces = snapshot.Namespaces;
		_version = snapshot.Version;
		_lastMergeKey = null;
		Selected = snapshot.Selection is { } path ? Root.FindByPath(path) : null;
		Changed?.Invoke(this, new DocumentChangedEventArgs(ChangeKind.Reset));
		SelectionChanged?.Invoke(this, EventArgs.Empty);
	}

	public string? GetNamespaceUri(string prefix)
	{
		foreach (var ns in Namespaces)
			if (ns.Prefix == prefix)
				return ns.Uri;
		return null;
	}

	public void EnsureNamespace(string prefix, string uri)
	{
		if (GetNamespaceUri(prefix) is null)
			Namespaces.Add(new XamlNamespace(prefix, uri));
	}

	public void SetNamespace(string prefix, string uri)
	{
		for (int i = 0; i < Namespaces.Count; i++)
		{
			if (Namespaces[i].Prefix == prefix)
			{
				Namespaces[i] = new XamlNamespace(prefix, uri);
				return;
			}
		}
		Namespaces.Add(new XamlNamespace(prefix, uri));
	}

	public string GetOrAddPrefixForClrNamespace(string clrNamespace, string preferredPrefix)
	{
		foreach (var ns in Namespaces)
			if (ns.Prefix.Length > 0 && XamlNs.ClrNamespaceOf(ns.Uri) == clrNamespace)
				return ns.Prefix;

		string prefix = preferredPrefix;
		for (int i = 2; GetNamespaceUri(prefix) is not null; i++)
			prefix = preferredPrefix + i;
		Namespaces.Add(new XamlNamespace(prefix, "using:" + clrNamespace));
		return prefix;
	}

	public string? ViewClass => Root.Get("x:Class");

	public string? ViewModelClass
	{
		get
		{
			string? dataType = Root.Get("x:DataType");
			if (string.IsNullOrWhiteSpace(dataType))
				return null;
			int colon = dataType.IndexOf(':');
			if (colon < 0)
				return dataType;
			string? clr = GetNamespaceUri(dataType[..colon]) is { } uri ? XamlNs.ClrNamespaceOf(uri) : null;
			string name = dataType[(colon + 1)..];
			return clr is null ? name : clr + "." + name;
		}
	}

	public void SetViewClass(string? fullName)
	{
		fullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
		if (fullName == ViewClass)
			return;
		Edit(() => Root.SetOrRemove("x:Class", fullName), ChangeKind.Attributes, "x:Class");
	}

	public void SetViewModelClass(string? fullName)
	{
		fullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
		if (fullName == ViewModelClass)
			return;
		Edit(() =>
		{
			if (fullName is null)
			{
				Root.Remove("x:DataType");
				return;
			}
			int dot = fullName.LastIndexOf('.');
			if (dot < 0)
			{
				Root.Set("x:DataType", fullName);
				return;
			}
			string prefix = GetOrAddPrefixForClrNamespace(fullName[..dot], "vm");
			Root.Set("x:DataType", prefix + ":" + fullName[(dot + 1)..]);
		}, ChangeKind.Attributes, "x:DataType");
	}

	public void SetAttribute(DesignNode node, string name, string? value, bool mergeTyping = false)
	{
		if (node.Get(name) == value)
			return;
		string? key = mergeTyping ? $"attr:{node.GetHashCode()}:{name}" : null;
		Edit(() => node.SetOrRemove(name, value), ChangeKind.Attributes, key);
	}

	public void SetAttributes(DesignNode node, IEnumerable<KeyValuePair<string, string?>> changes, string? mergeKey = null)
	{
		var list = changes.Where(c => node.Get(c.Key) != c.Value).ToList();
		if (list.Count == 0)
			return;
		Edit(() =>
		{
			foreach (var (name, value) in list)
				node.SetOrRemove(name, value);
		}, ChangeKind.Attributes, mergeKey);
	}

	public void SetText(DesignNode node, string? text)
	{
		if (string.IsNullOrEmpty(text))
			text = null;
		if (node.Text == text)
			return;
		Edit(() => node.Text = text, ChangeKind.Attributes, $"text:{node.GetHashCode()}");
	}

	public void InsertNode(DesignNode parent, DesignNode node, int? contentIndex = null, bool select = true)
	{
		Edit(() =>
		{
			if (contentIndex is { } index)
				parent.InsertContent(index, node);
			else
				parent.AddContent(node);
			CleanPlacement(node);
			MakeNamesUnique(node);
			parent.IsExpanded = true;
			if (select)
				Selected = node;
		});
	}

	public void RemoveNode(DesignNode node)
	{
		var parent = node.Parent;
		if (parent is null)
			return;
		Edit(() =>
		{
			int index = node.Index;
			parent.RemoveChild(node);
			DesignNode? next = index < parent.Children.Count ? parent.Children[index]
				: index > 0 ? parent.Children[index - 1]
				: parent;
			Selected = next;
		});
	}

	public void MoveNode(DesignNode node, DesignNode newParent, int? contentIndex,
		IEnumerable<KeyValuePair<string, string?>>? attributeChanges = null)
	{
		if (node.Parent is null || node == newParent || newParent.IsDescendantOf(node))
			return;
		Edit(() =>
		{
			node.Detach();
			if (contentIndex is { } index)
				newParent.InsertContent(index, node);
			else
				newParent.AddContent(node);
			CleanPlacement(node);
			if (attributeChanges is not null)
				foreach (var (name, value) in attributeChanges)
					node.SetOrRemove(name, value);
			newParent.IsExpanded = true;
			Selected = node;
		});
	}

	public bool MoveInParent(DesignNode node, int delta)
	{
		var parent = node.Parent;
		if (parent is null || node.IsPropertyElement)
			return false;
		int current = parent.ContentIndexOf(node);
		int target = Math.Clamp(current + delta, 0, parent.ContentCount - 1);
		if (target == current)
			return false;
		Edit(() =>
		{
			node.Detach();
			parent.InsertContent(target, node);
		});
		return true;
	}

	public DesignNode? Duplicate(DesignNode node)
	{
		var parent = node.Parent;
		if (parent is null)
			return null;
		var copy = node.Clone();
		Edit(() =>
		{
			parent.Insert(node.Index + 1, copy);
			MakeNamesUnique(copy);
			Selected = copy;
		});
		return copy;
	}

	public void Wrap(DesignNode node, DesignNode wrapper)
	{
		var parent = node.Parent;
		if (parent is null)
			return;
		Edit(() =>
		{
			int index = node.Index;
			parent.RemoveChild(node);
			foreach (var attribute in node.Attributes.ToList())
			{
				if (IsPlacementAttribute(attribute.Name))
				{
					wrapper.Set(attribute.Name, attribute.Value);
					node.Remove(attribute.Name);
				}
			}
			parent.Insert(index, wrapper);
			wrapper.AddContent(node);
			Selected = wrapper;
		});
	}

	public void Unwrap(DesignNode node)
	{
		var parent = node.Parent;
		if (parent is null)
			return;
		Edit(() =>
		{
			int index = node.Index;
			var children = node.ContentChildren.ToList();
			parent.RemoveChild(node);
			DesignNode? first = null;
			foreach (var child in children)
			{
				child.Detach();
				CleanPlacement(child, parent);
				parent.Insert(index++, child);
				first ??= child;
			}
			if (first is not null)
			{
				foreach (var attribute in node.Attributes)
					if (IsPlacementAttribute(attribute.Name))
						first.Set(attribute.Name, attribute.Value);
			}
			Selected = first ?? parent;
		});
	}

	public void ReplaceContent(DesignNode newRoot, IEnumerable<XamlNamespace> namespaces)
	{
		int[]? selection = Selected?.GetPath();
		Edit(() =>
		{
			Root = newRoot;
			Namespaces = namespaces.ToList();
			EnsureNamespace("", XamlNs.Avalonia);
			EnsureNamespace("x", XamlNs.Xaml);
			Selected = selection is null ? null : Root.FindByPath(selection);
		}, ChangeKind.Reset, "xaml-text");
	}

	public static bool IsPlacementAttribute(string name) => name switch
	{
		"Grid.Row" or "Grid.Column" or "Grid.RowSpan" or "Grid.ColumnSpan" => true,
		"DockPanel.Dock" => true,
		"Canvas.Left" or "Canvas.Top" or "Canvas.Right" or "Canvas.Bottom" => true,
		_ => name.StartsWith("RelativePanel.", StringComparison.Ordinal),
	};

	private static void CleanPlacement(DesignNode node, DesignNode? parent = null)
	{
		parent ??= node.Parent;
		string owner = parent is null || parent.IsPropertyElement ? "" : parent.TypeName;
		foreach (var attribute in node.Attributes.ToList())
		{
			if (!IsPlacementAttribute(attribute.Name))
				continue;
			string attributeOwner = attribute.Name[..attribute.Name.IndexOf('.')];
			if (attributeOwner != owner)
				node.Remove(attribute.Name);
		}
	}

	private void MakeNamesUnique(DesignNode added)
	{
		var inAdded = new HashSet<DesignNode>(added.SelfAndDescendants());
		var used = new HashSet<string>(StringComparer.Ordinal);
		foreach (var node in Root.SelfAndDescendants())
			if (!inAdded.Contains(node) && node.Name is { } name)
				used.Add(name);

		foreach (var node in added.SelfAndDescendants())
		{
			if (node.Name is not { } name)
				continue;
			if (used.Add(name))
				continue;
			string stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
			if (stem.Length == 0)
				stem = name;
			string candidate = name;
			for (int i = 2; !used.Add(candidate = stem + i); i++)
			{
			}
			string attribute = node.Has("x:Name") ? "x:Name" : "Name";
			node.Set(attribute, candidate);
		}
	}
}
