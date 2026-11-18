using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Designer;

internal sealed class DropTarget
{
	public DropTarget(DesignNode container, Rect bounds)
	{
		Container = container;
		Bounds = bounds;
	}

	public DesignNode Container { get; }

	public Rect Bounds { get; }

	public int? Index { get; set; }

	public Rect? Highlight { get; set; }

	public (Point From, Point To)? Line { get; set; }

	public List<KeyValuePair<string, string?>> Attributes { get; } = new();

	public string? Hint { get; set; }
}

public partial class DesignSurface
{
	private enum DragMode
	{
		None,
		Pending,
		Move,
		Resize,
		ResizeRoot,
		Tool,
		Pan,
	}

	[Flags]
	private enum Handle
	{
		None = 0,
		Left = 1,
		Top = 2,
		Right = 4,
		Bottom = 8,
	}

	private const double HandleSize = 7;
	private const double HandleReach = 6;

	private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
	private static readonly Cursor NoCursor = new(StandardCursorType.No);
	private static readonly Cursor HorizontalCursor = new(StandardCursorType.SizeWestEast);
	private static readonly Cursor VerticalCursor = new(StandardCursorType.SizeNorthSouth);
	private static readonly Cursor DiagonalDownCursor = new(StandardCursorType.BottomRightCorner);
	private static readonly Cursor DiagonalUpCursor = new(StandardCursorType.TopRightCorner);

	private DragMode _mode;
	private DesignNode? _hover;
	private DesignNode? _dragNode;
	private Handle _handle;
	private Point _pressPoint;
	private Point _lastPoint;
	private Point _grabOffset;
	private Size _dragSize;
	private Size _startSize;
	private double _startLeft;
	private double _startTop;
	private Point _panStartPoint;
	private Vector _panStartOffset;
	private DropTarget? _dropTarget;
	private bool _parentWanted;
	private List<DesignNode>? _toolNodes;
	private List<XamlNamespace>? _toolNamespaces;
	private string? _toolLabel;

	private void OnOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (_document is null || _preview is null)
			return;
		Overlay.Focus();
		var point = e.GetPosition(Overlay);
		var properties = e.GetCurrentPoint(Overlay).Properties;

		if (properties.IsMiddleButtonPressed)
		{
			_mode = DragMode.Pan;
			_panStartPoint = e.GetPosition(this);
			_panStartOffset = Scroller.Offset;
			e.Pointer.Capture(Overlay);
			e.Handled = true;
			return;
		}

		if (properties.IsRightButtonPressed)
		{
			_document.Select(HitTest(point) ?? _document.Root);
			return;
		}

		if (!properties.IsLeftButtonPressed)
			return;
		e.Handled = true;

		if (_document.Selected is { } selected && HandleAt(selected, point) is var handle and not Handle.None)
		{
			BeginResize(selected, handle, point);
			if (_mode != DragMode.None)
				e.Pointer.Capture(Overlay);
			return;
		}

		var node = HitTest(point) ?? _document.Root;
		var previewBefore = _preview;
		_document.Select(node);
		if (!ReferenceEquals(previewBefore, _preview))
			return;

		if (node != _document.Root && BoundsOf(node) is { } rect)
		{
			_mode = DragMode.Pending;
			_dragNode = node;
			_pressPoint = point;
			_lastPoint = point;
			_grabOffset = point - rect.TopLeft;
			_dragSize = rect.Size;
			e.Pointer.Capture(Overlay);
		}
	}

	private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
	{
		if (_document is null || _preview is null)
			return;
		var point = e.GetPosition(Overlay);
		_lastPoint = point;

		switch (_mode)
		{
			case DragMode.None:
				var hover = HitTest(point);
				if (hover != _hover)
				{
					_hover = hover;
					Overlay.InvalidateVisual();
				}
				Overlay.Cursor = _document.Selected is { } selected ? CursorFor(HandleAt(selected, point)) : null;
				break;

			case DragMode.Pending:
				if (Math.Abs(point.X - _pressPoint.X) < 4 && Math.Abs(point.Y - _pressPoint.Y) < 4)
					break;
				_mode = DragMode.Move;
				goto case DragMode.Move;

			case DragMode.Move:
				_parentWanted = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
				_dropTarget = _dragNode is null ? null : FindDropTarget(point, _dragNode, _dragNode);
				Overlay.Cursor = _dropTarget is null ? NoCursor : MoveCursor;
				Overlay.InvalidateVisual();
				break;

			case DragMode.Resize:
				UpdateResize(point);
				Overlay.InvalidateVisual();
				break;

			case DragMode.ResizeRoot:
				UpdateRootResize(point);
				Overlay.InvalidateVisual();
				break;

			case DragMode.Pan:
				var delta = e.GetPosition(this) - _panStartPoint;
				Scroller.Offset = new Vector(Math.Max(0, _panStartOffset.X - delta.X), Math.Max(0, _panStartOffset.Y - delta.Y));
				break;
		}
	}

	private void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		var mode = _mode;
		var node = _dragNode;
		var target = _dropTarget;
		_mode = DragMode.None;
		_dragNode = null;
		_dropTarget = null;
		e.Pointer.Capture(null);
		Overlay.Cursor = null;
		if (_document is null)
			return;

		switch (mode)
		{
			case DragMode.Move:
				if (node is not null && target is not null)
					ApplyMove(node, target);
				break;
			case DragMode.Resize:
				if (node is not null)
					CommitResize(node);
				break;
			case DragMode.ResizeRoot:
				CommitRootResize();
				break;
		}
		Overlay.InvalidateVisual();
	}

	private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		if (_mode is not (DragMode.None or DragMode.Tool))
			CancelDrag();
	}

	private void OnOverlayPointerExited(object? sender, PointerEventArgs e)
	{
		if (_hover is not null && _mode == DragMode.None)
		{
			_hover = null;
			Overlay.InvalidateVisual();
		}
	}

	private void OnOverlayDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (_document?.Selected is { } selected)
			EditRequested?.Invoke(this, selected);
	}

	public void CancelDrag()
	{
		bool hadLiveChanges = _mode is DragMode.Resize or DragMode.ResizeRoot;
		_mode = DragMode.None;
		_dragNode = null;
		_dropTarget = null;
		_toolNodes = null;
		_toolNamespaces = null;
		_toolLabel = null;
		Overlay.Cursor = null;
		if (hadLiveChanges)
			Rebuild();
		else
			Overlay.InvalidateVisual();
	}

	public bool IsDragging => _mode is not (DragMode.None or DragMode.Pending);

	private void ApplyMove(DesignNode node, DropTarget target)
	{
		if (_document is null)
			return;
		bool sameParent = node.Parent == target.Container;
		int? index = target.Index;
		if (sameParent)
		{
			int current = target.Container.ContentIndexOf(node);
			index ??= current;
			if (index == current && target.Attributes.All(a => node.Get(a.Key) == a.Value))
				return;
		}
		_document.MoveNode(node, target.Container, index, target.Attributes);
	}

	private static IEnumerable<(Handle Handle, Point Point)> HandlePoints(Rect rect, bool rootOnly)
	{
		if (!rootOnly)
		{
			yield return (Handle.Left | Handle.Top, rect.TopLeft);
			yield return (Handle.Top, new Point(rect.Center.X, rect.Top));
			yield return (Handle.Right | Handle.Top, rect.TopRight);
			yield return (Handle.Left, new Point(rect.Left, rect.Center.Y));
			yield return (Handle.Left | Handle.Bottom, rect.BottomLeft);
		}
		yield return (Handle.Right, new Point(rect.Right, rect.Center.Y));
		yield return (Handle.Bottom, new Point(rect.Center.X, rect.Bottom));
		yield return (Handle.Right | Handle.Bottom, rect.BottomRight);
	}

	private Handle HandleAt(DesignNode node, Point point)
	{
		if (_document is null || BoundsOf(node) is not { } rect)
			return Handle.None;
		foreach (var (handle, at) in HandlePoints(rect, node == _document.Root))
			if (Math.Abs(point.X - at.X) <= HandleReach && Math.Abs(point.Y - at.Y) <= HandleReach)
				return handle;
		return Handle.None;
	}

	private static Cursor? CursorFor(Handle handle) => handle switch
	{
		Handle.Left or Handle.Right => HorizontalCursor,
		Handle.Top or Handle.Bottom => VerticalCursor,
		Handle.Left | Handle.Top or Handle.Right | Handle.Bottom => DiagonalDownCursor,
		Handle.Right | Handle.Top or Handle.Left | Handle.Bottom => DiagonalUpCursor,
		_ => null,
	};

	private void BeginResize(DesignNode node, Handle handle, Point point)
	{
		if (_document is null)
			return;
		_dragNode = node;
		_handle = handle;
		_pressPoint = point;
		_lastPoint = point;

		if (node == _document.Root)
		{
			_startSize = GetDesignSize();
			_mode = DragMode.ResizeRoot;
			return;
		}
		if (ControlOf(node) is not { } control)
			return;
		_startSize = control.Bounds.Size;
		_startLeft = Canvas.GetLeft(control);
		_startTop = Canvas.GetTop(control);
		_mode = DragMode.Resize;
	}

	private (double Width, double Height, double MoveX, double MoveY) ResizeValues(Point point)
	{
		var delta = (point - _pressPoint) / Zoom;
		double width = _startSize.Width;
		double height = _startSize.Height;
		double moveX = 0;
		double moveY = 0;
		if (_handle.HasFlag(Handle.Right))
			width += delta.X;
		if (_handle.HasFlag(Handle.Left))
		{
			width -= delta.X;
			moveX = delta.X;
		}
		if (_handle.HasFlag(Handle.Bottom))
			height += delta.Y;
		if (_handle.HasFlag(Handle.Top))
		{
			height -= delta.Y;
			moveY = delta.Y;
		}
		return (Math.Max(1, Math.Round(width)), Math.Max(1, Math.Round(height)), Math.Round(moveX), Math.Round(moveY));
	}

	private bool ResizesWidth => (_handle & (Handle.Left | Handle.Right)) != 0;

	private bool ResizesHeight => (_handle & (Handle.Top | Handle.Bottom)) != 0;

	private static bool IsInCanvas(DesignNode node) => node.Parent?.TypeName == "Canvas";

	private void UpdateResize(Point point)
	{
		if (_dragNode is null || ControlOf(_dragNode) is not { } control)
			return;
		var (width, height, moveX, moveY) = ResizeValues(point);
		if (ResizesWidth)
			control.Width = width;
		if (ResizesHeight)
			control.Height = height;
		if (IsInCanvas(_dragNode))
		{
			if (_handle.HasFlag(Handle.Left))
				Canvas.SetLeft(control, (double.IsNaN(_startLeft) ? 0 : _startLeft) + moveX);
			if (_handle.HasFlag(Handle.Top))
				Canvas.SetTop(control, (double.IsNaN(_startTop) ? 0 : _startTop) + moveY);
		}
	}

	private void CommitResize(DesignNode node)
	{
		if (_document is null)
			return;
		var (width, height, moveX, moveY) = ResizeValues(_lastPoint);
		var changes = new List<KeyValuePair<string, string?>>();
		if (ResizesWidth && width != Math.Round(_startSize.Width))
			changes.Add(new("Width", ValueFormatter.FormatNumber(width)));
		if (ResizesHeight && height != Math.Round(_startSize.Height))
			changes.Add(new("Height", ValueFormatter.FormatNumber(height)));
		if (IsInCanvas(node))
		{
			if (_handle.HasFlag(Handle.Left) && moveX != 0)
				changes.Add(new("Canvas.Left", ValueFormatter.FormatNumber((double.IsNaN(_startLeft) ? 0 : _startLeft) + moveX)));
			if (_handle.HasFlag(Handle.Top) && moveY != 0)
				changes.Add(new("Canvas.Top", ValueFormatter.FormatNumber((double.IsNaN(_startTop) ? 0 : _startTop) + moveY)));
		}

		if (changes.Count == 0 || changes.All(c => node.Get(c.Key) == c.Value))
			Rebuild();
		else
			_document.SetAttributes(node, changes);
	}

	private void UpdateRootResize(Point point)
	{
		var (width, height, _, _) = ResizeValues(point);
		if (ResizesWidth)
			ClientArea.Width = width;
		if (ResizesHeight)
			ClientArea.Height = height;
		if (_preview?.Root is { } root && _document is not null)
		{
			if (ResizesWidth && _document.Root.Has("Width"))
				root.Width = width;
			if (ResizesHeight && _document.Root.Has("Height"))
				root.Height = height;
		}
	}

	private void CommitRootResize()
	{
		if (_document is null)
			return;
		var (width, height, _, _) = ResizeValues(_lastPoint);
		var root = _document.Root;
		bool isWindow = DesignRules.IsWindow(XamlTypes.Resolve(_document, root));
		bool changeWidth = ResizesWidth && width != Math.Round(_startSize.Width);
		bool changeHeight = ResizesHeight && height != Math.Round(_startSize.Height);
		if (!changeWidth && !changeHeight)
		{
			Rebuild();
			return;
		}

		_document.Edit(() =>
		{
			void Set(string realName, string designName, double value)
			{
				if (isWindow || root.Has(realName))
				{
					root.Set(realName, ValueFormatter.FormatNumber(value));
					return;
				}
				_document.EnsureNamespace("d", XamlNs.Design);
				_document.EnsureNamespace("mc", XamlNs.Compatibility);
				if (!root.Has("mc:Ignorable"))
					root.Set("mc:Ignorable", "d");
				root.Set(designName, ValueFormatter.FormatNumber(value));
			}

			if (changeWidth)
				Set("Width", "d:DesignWidth", width);
			if (changeHeight)
				Set("Height", "d:DesignHeight", height);
		}, ChangeKind.Attributes);
	}

	private void OnOverlayKeyDown(object? sender, KeyEventArgs e)
	{
		if (_document is null)
			return;
		var selected = _document.Selected;

		if (e.Key == Key.Escape)
		{
			if (_mode != DragMode.None)
				CancelDrag();
			else if (selected?.ObjectParent is { } parent)
				_document.Select(parent);
			e.Handled = true;
			return;
		}

		if (selected is null || _mode != DragMode.None || e.KeyModifiers.HasFlag(KeyModifiers.Control))
			return;
		int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
		int dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
		if (dx == 0 && dy == 0)
			return;
		e.Handled = true;

		if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
		{
			_document.MoveInParent(selected, dx + dy);
			return;
		}

		if (IsInCanvas(selected))
		{
			double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
			if (TryNumber(selected.Get("Canvas.Left"), out double left) && TryNumber(selected.Get("Canvas.Top"), out double top))
			{
				_document.SetAttributes(selected, new KeyValuePair<string, string?>[]
				{
					new("Canvas.Left", ValueFormatter.FormatNumber(left + dx * step)),
					new("Canvas.Top", ValueFormatter.FormatNumber(top + dy * step)),
				}, mergeKey: "nudge:" + selected.GetHashCode());
			}
			return;
		}

		DesignNode? next;
		if (dy != 0)
		{
			var siblings = selected.Parent?.ContentChildren.ToList();
			int index = siblings?.IndexOf(selected) ?? -1;
			next = siblings is not null && index + dy >= 0 && index + dy < siblings.Count ? siblings[index + dy] : null;
		}
		else
		{
			next = dx < 0 ? selected.ObjectParent : selected.ContentChildren.FirstOrDefault();
		}
		if (next is not null)
			_document.Select(next);
	}

	private static bool TryNumber(string? text, out double value)
	{
		value = 0;
		return text is null || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	public void BeginToolDrag(ToolboxItem item)
	{
		if (_document is null || _preview?.Root is null || IsRunMode)
			return;
		CancelDrag();
		try
		{
			_toolNodes = item.Create(_document, out _toolNamespaces);
		}
		catch (Exception)
		{
			_toolNodes = null;
		}
		if (_toolNodes is not { Count: > 0 })
			return;
		_toolLabel = item.Name;
		_mode = DragMode.Tool;
	}

	public bool UpdateToolDrag(PointerEventArgs e)
	{
		if (_mode != DragMode.Tool || _toolNodes is null)
			return false;
		_lastPoint = e.GetPosition(Overlay);
		_parentWanted = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
		bool overSurface = new Rect(Scroller.Bounds.Size).Contains(e.GetPosition(Scroller));
		_dropTarget = overSurface ? FindDropTarget(_lastPoint, null, _toolNodes[0]) : null;
		Overlay.InvalidateVisual();
		return _dropTarget is not null;
	}

	public bool CompleteToolDrag()
	{
		if (_mode != DragMode.Tool || _document is null)
			return false;
		var target = _dropTarget;
		var nodes = _toolNodes;
		var namespaces = _toolNamespaces;
		CancelDrag();
		if (target is null || nodes is null)
			return false;
		DesignActions.Insert(_document, target.Container, target.Index, nodes, namespaces, target.Attributes);
		Overlay.Focus();
		return true;
	}

	internal DropTarget? FindDropTarget(Point point, DesignNode? moving, DesignNode payload)
	{
		if (_document is null || _preview is null || !RootBounds().Contains(point))
			return null;

		var candidates = new List<DesignNode>();
		for (DesignNode? node = HitTest(point, moving) ?? _document.Root; node is not null; node = node.Parent)
		{
			if (node.IsPropertyElement)
				continue;
			var type = XamlTypes.Resolve(_document, node);
			if (type is null || !DesignRules.IsDropContainer(type))
				continue;

			if (DesignRules.CanAddChild(_document, node, payload, moving))
				candidates.Add(node);
			else if (type == typeof(TabControl) && OpenTabTarget(node, payload, moving) is { } inTab && !candidates.Contains(inTab))
				candidates.Add(inTab);
		}
		if (candidates.Count == 0)
			return null;

		int index = 0;
		while (index < candidates.Count - 1 && IsNearEdge(candidates[index], point))
			index++;
		if (_parentWanted)
			index = Math.Min(index + 1, candidates.Count - 1);
		return MakeDropTarget(candidates[index], point, moving);
	}

	private const double EdgeZone = 8;

	private bool IsNearEdge(DesignNode node, Point point)
	{
		if (VisibleBoundsOf(node) is not { } rect)
			return false;
		return point.X - rect.Left < EdgeZone || rect.Right - point.X < EdgeZone
			|| point.Y - rect.Top < EdgeZone || rect.Bottom - point.Y < EdgeZone;
	}

	private DesignNode? OpenTabTarget(DesignNode tabControl, DesignNode payload, DesignNode? moving)
	{
		var tabs = tabControl.ContentChildren.ToList();
		int index = _activeTabs.TryGetValue(tabControl, out int active) ? active
			: int.TryParse(tabControl.Get("SelectedIndex"), out int fromAttribute) ? fromAttribute
			: 0;
		if (index < 0 || index >= tabs.Count)
			return null;
		var tab = tabs[index];
		if (DesignRules.CanAddChild(_document!, tab, payload, moving))
			return tab;
		var inner = tab.ContentChildren.FirstOrDefault();
		return inner is not null && inner != moving && DesignRules.CanAddChild(_document!, inner, payload, moving) ? inner : null;
	}

	private DropTarget MakeDropTarget(DesignNode container, Point point, DesignNode? moving)
	{
		var bounds = VisibleBoundsOf(container) ?? RootBounds();
		var target = new DropTarget(container, bounds);
		var control = container == _document!.Root ? _preview!.Root : ControlOf(container);
		int? keepIndex = moving?.Parent == container ? container.ContentIndexOf(moving) : null;

		switch (control)
		{
			case Canvas canvas when Overlay.TranslatePoint(point, canvas) is { } local:
				{
					var grab = moving is null ? default : new Point(_grabOffset.X / Zoom, _grabOffset.Y / Zoom);
					double left = Math.Round(local.X - grab.X);
					double top = Math.Round(local.Y - grab.Y);
					target.Attributes.Add(new("Canvas.Left", ValueFormatter.FormatNumber(left)));
					target.Attributes.Add(new("Canvas.Top", ValueFormatter.FormatNumber(top)));
					target.Index = keepIndex;
					target.Hint = $"x {left:0}, y {top:0}";
					break;
				}

			case Grid grid when Overlay.TranslatePoint(point, grid) is { } local:
				{
					var (columns, rows) = GridTracks(grid);
					int column = TrackAt(columns, local.X);
					int row = TrackAt(rows, local.Y);
					target.Attributes.Add(new("Grid.Row", row == 0 ? null : row.ToString(CultureInfo.InvariantCulture)));
					target.Attributes.Add(new("Grid.Column", column == 0 ? null : column.ToString(CultureInfo.InvariantCulture)));
					target.Index = keepIndex;
					if (grid.TransformToVisual(Overlay) is { } matrix)
					{
						var cell = new Rect(columns[column].Start, rows[row].Start, columns[column].Length, rows[row].Length);
						target.Highlight = cell.TransformToAABB(matrix);
					}
					if (columns.Count > 1 || rows.Count > 1)
						target.Hint = $"row {row}, column {column}";
					break;
				}

			case DockPanel dock when Overlay.TranslatePoint(point, dock) is { } local:
				{
					var (side, zone) = DockZone(dock.Bounds.Size, local);
					target.Attributes.Add(new("DockPanel.Dock", side));
					if (dock.TransformToVisual(Overlay) is { } matrix)
						target.Highlight = zone.TransformToAABB(matrix);
					target.Hint = side is null ? "fill" : "dock " + side.ToLowerInvariant();

					var others = container.ContentChildren.Where(c => c != moving).ToList();
					bool lastFills = dock.LastChildFill && others.Count > 0 && !others[^1].Has("DockPanel.Dock");
					target.Index = side is not null && lastFills ? others.Count - 1 : null;
					break;
				}

			case RelativePanel:
				target.Index = keepIndex;
				break;

			case Panel panel when panel.GetType() == typeof(Panel):
				target.Index = keepIndex;
				break;

			default:
				PlaceInRow(target, control, point, moving);
				break;
		}
		return target;
	}

	private void PlaceInRow(DropTarget target, Control? control, Point point, DesignNode? moving)
	{
		var children = target.Container.ContentChildren.Where(c => c != moving).ToList();
		var placed = new List<(DesignNode Node, Rect Rect)>();
		foreach (var child in children)
			if (BoundsOf(child) is { } rect)
				placed.Add((child, rect));
		if (placed.Count == 0)
			return;

		var nearest = placed[0];
		double best = double.MaxValue;
		foreach (var item in placed)
		{
			double distanceX = Math.Max(Math.Max(item.Rect.Left - point.X, 0), point.X - item.Rect.Right);
			double distanceY = Math.Max(Math.Max(item.Rect.Top - point.Y, 0), point.Y - item.Rect.Bottom);
			double distance = distanceX * distanceX + distanceY * distanceY;
			if (distance < best)
			{
				best = distance;
				nearest = item;
			}
		}

		bool horizontal = control switch
		{
			StackPanel stack => stack.Orientation == Orientation.Horizontal,
			WrapPanel wrap => wrap.Orientation == Orientation.Horizontal,
			Menu or TabControl or UniformGrid => true,
			_ => placed.Count > 1
				 && Math.Abs(placed[1].Rect.Center.X - placed[0].Rect.Center.X) > Math.Abs(placed[1].Rect.Center.Y - placed[0].Rect.Center.Y),
		};

		var r = nearest.Rect;
		bool before = horizontal ? point.X < r.Center.X : point.Y < r.Center.Y;
		target.Index = children.IndexOf(nearest.Node) + (before ? 0 : 1);
		if (horizontal)
		{
			double x = before ? r.Left - 2 : r.Right + 2;
			target.Line = (new Point(x, r.Top), new Point(x, r.Bottom));
		}
		else
		{
			double y = before ? r.Top - 2 : r.Bottom + 2;
			target.Line = (new Point(Math.Min(r.Left, target.Bounds.Left + 4), y), new Point(Math.Max(r.Right, target.Bounds.Right - 4), y));
		}
	}

	internal static (List<(double Start, double Length)> Columns, List<(double Start, double Length)> Rows) GridTracks(Grid grid)
	{
		static List<(double Start, double Length)> Build(IEnumerable<double> lengths, double total, double spacing)
		{
			var tracks = new List<(double Start, double Length)>();
			double position = 0;
			foreach (double length in lengths)
			{
				tracks.Add((position, length));
				position += length + spacing;
			}
			if (tracks.Count == 0)
				tracks.Add((0, total));
			return tracks;
		}

		return (
			Build(grid.ColumnDefinitions.Select(c => c.ActualWidth), grid.Bounds.Width, grid.ColumnSpacing),
			Build(grid.RowDefinitions.Select(r => r.ActualHeight), grid.Bounds.Height, grid.RowSpacing));
	}

	private static int TrackAt(List<(double Start, double Length)> tracks, double position)
	{
		for (int i = tracks.Count - 1; i > 0; i--)
			if (position >= tracks[i].Start)
				return i;
		return 0;
	}

	private static (string? Side, Rect Zone) DockZone(Size size, Point local)
	{
		double bandX = Math.Max(1, Math.Min(90, size.Width * 0.25));
		double bandY = Math.Max(1, Math.Min(90, size.Height * 0.25));
		double left = local.X / bandX;
		double right = (size.Width - local.X) / bandX;
		double top = local.Y / bandY;
		double bottom = (size.Height - local.Y) / bandY;
		double nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

		if (nearest >= 1)
			return (null, new Rect(bandX, bandY, Math.Max(0, size.Width - 2 * bandX), Math.Max(0, size.Height - 2 * bandY)));
		if (nearest == top)
			return ("Top", new Rect(0, 0, size.Width, bandY));
		if (nearest == bottom)
			return ("Bottom", new Rect(0, size.Height - bandY, size.Width, bandY));
		if (nearest == left)
			return ("Left", new Rect(0, 0, bandX, size.Height));
		return ("Right", new Rect(size.Width - bandX, 0, bandX, size.Height));
	}
}
