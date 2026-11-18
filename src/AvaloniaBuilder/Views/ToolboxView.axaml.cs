using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaBuilder.Metadata;

namespace AvaloniaBuilder.Views;

public partial class ToolboxView : UserControl
{
	private static readonly Cursor CopyCursor = new(StandardCursorType.DragCopy);
	private static readonly Cursor NoCursor = new(StandardCursorType.No);

	private ToolboxItem? _pressedItem;
	private Point _pressPoint;
	private bool _dragging;

	public ToolboxView()
	{
		InitializeComponent();
		AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
		AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
		AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
		AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
		AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
	}

	public event Action<ToolboxItem>? DragStarted;

	public event Func<PointerEventArgs, bool>? DragMoved;

	public event Action? DragCompleted;

	public event Action? DragCanceled;

	public event Action<ToolboxItem>? ItemActivated;

	private static ToolboxItem? ItemAt(object? source) => (source as StyledElement)?.DataContext as ToolboxItem;

	private void OnPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;
		_pressedItem = ItemAt(e.Source);
		_pressPoint = e.GetPosition(this);
	}

	private void OnMoved(object? sender, PointerEventArgs e)
	{
		if (_pressedItem is null)
			return;

		if (!_dragging)
		{
			var point = e.GetPosition(this);
			if (Math.Abs(point.X - _pressPoint.X) < 5 && Math.Abs(point.Y - _pressPoint.Y) < 5)
				return;
			_dragging = true;
			e.Pointer.Capture(this);
			DragStarted?.Invoke(_pressedItem);
		}

		bool canDrop = DragMoved?.Invoke(e) ?? false;
		Cursor = canDrop ? CopyCursor : NoCursor;
	}

	private void OnReleased(object? sender, PointerReleasedEventArgs e)
	{
		bool wasDragging = _dragging;
		_pressedItem = null;
		_dragging = false;
		Cursor = null;
		if (!wasDragging)
			return;
		e.Pointer.Capture(null);
		DragCompleted?.Invoke();
	}

	private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
	{
		if (!_dragging)
			return;
		_pressedItem = null;
		_dragging = false;
		Cursor = null;
		DragCanceled?.Invoke();
	}

	private void OnDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (ItemAt(e.Source) is { } item)
			ItemActivated?.Invoke(item);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (e.Key == Key.Escape && _dragging)
		{
			_pressedItem = null;
			_dragging = false;
			Cursor = null;
			DragCanceled?.Invoke();
		}
	}
}
