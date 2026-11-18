using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaBuilder.ViewModels;

namespace AvaloniaBuilder.Views;

public partial class OutlineView : UserControl
{
	private OutlineRow? _pressedRow;
	private Point _pressPoint;
	private bool _dragging;
	private OutlineRow? _targetRow;
	private DropPlace _place;

	public OutlineView()
	{
		InitializeComponent();
		List.AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
		List.AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
		List.AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
		List.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Bubble, handledEventsToo: true);
	}

	private OutlineViewModel? ViewModel => DataContext as OutlineViewModel;

	private void OnPressed(object? sender, PointerPressedEventArgs e)
	{
		_pressedRow = null;
		if (!e.GetCurrentPoint(List).Properties.IsLeftButtonPressed)
			return;
		if (e.Source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null)
			return;
		_pressedRow = (e.Source as StyledElement)?.DataContext as OutlineRow;
		_pressPoint = e.GetPosition(List);
	}

	private void OnMoved(object? sender, PointerEventArgs e)
	{
		if (_pressedRow is null || ViewModel is null)
			return;
		var point = e.GetPosition(List);
		if (!_dragging)
		{
			if (Math.Abs(point.Y - _pressPoint.Y) < 5 && Math.Abs(point.X - _pressPoint.X) < 5)
				return;
			_dragging = true;
			e.Pointer.Capture(List);
		}
		UpdateTarget(point);
	}

	private void UpdateTarget(Point point)
	{
		_targetRow = null;
		DropLine.IsVisible = false;
		DropBox.IsVisible = false;
		if (ViewModel is null || _pressedRow is null)
			return;

		foreach (var row in ViewModel.Rows)
		{
			if (List.ContainerFromItem(row) is not { } container || container.TranslatePoint(default, List) is not { } origin)
				continue;
			var rect = new Rect(origin, container.Bounds.Size);
			if (point.Y < rect.Top || point.Y >= rect.Bottom)
				continue;

			double fraction = (point.Y - rect.Top) / rect.Height;
			DropPlace wish = fraction < 0.25 ? DropPlace.Before : fraction > 0.75 ? DropPlace.After : DropPlace.Inside;
			DropPlace[] order = wish == DropPlace.Inside
				? new[] { DropPlace.Inside, fraction < 0.5 ? DropPlace.Before : DropPlace.After }
				: new[] { wish, DropPlace.Inside };
			foreach (var place in order)
			{
				if (!ViewModel.CanDrop(_pressedRow.Node, row.Node, place))
					continue;
				_targetRow = row;
				_place = place;
				ShowIndicator(rect, place, row.Indent.Left);
				return;
			}
			return;
		}
	}

	private void ShowIndicator(Rect rowRect, DropPlace place, double indent)
	{
		if (place == DropPlace.Inside)
		{
			DropBox.Margin = new Thickness(indent + 4, rowRect.Top, 4, 0);
			DropBox.Height = rowRect.Height;
			DropBox.IsVisible = true;
		}
		else
		{
			double y = place == DropPlace.Before ? rowRect.Top - 1 : rowRect.Bottom - 1;
			DropLine.Margin = new Thickness(indent + 20, Math.Max(0, y), 4, 0);
			DropLine.IsVisible = true;
		}
	}

	private void OnReleased(object? sender, PointerReleasedEventArgs e)
	{
		var source = _pressedRow;
		var target = _targetRow;
		var place = _place;
		bool wasDragging = _dragging;
		EndDrag();
		if (wasDragging)
			e.Pointer.Capture(null);
		if (wasDragging && source is not null && target is not null)
			ViewModel?.Drop(source.Node, target.Node, place);
	}

	private void EndDrag()
	{
		_pressedRow = null;
		_targetRow = null;
		_dragging = false;
		DropLine.IsVisible = false;
		DropBox.IsVisible = false;
	}
}
