using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Designer;

public partial class DesignSurface
{
	private static readonly Color AccentColor = Color.Parse("#1E88E5");
	private static readonly Color DropColor = Color.Parse("#2EA043");
	private static readonly ImmutableSolidColorBrush AccentBrush = new ImmutableSolidColorBrush(AccentColor);
	private static readonly ImmutableSolidColorBrush DropBrush = new ImmutableSolidColorBrush(DropColor);
	private static readonly IBrush DropFill = new ImmutableSolidColorBrush(DropColor, 0.18);
	private static readonly IBrush GhostFill = new ImmutableSolidColorBrush(AccentColor, 0.12);
	private static readonly IBrush HintTextBrush = new ImmutableSolidColorBrush(Color.FromRgb(128, 128, 128), 0.9);
	private static readonly IBrush GridLabelBrush = new ImmutableSolidColorBrush(AccentColor, 0.95);
	private static readonly ImmutableDashStyle Dashes = new ImmutableDashStyle(new double[] { 4, 3 }, 0);
	private static readonly IPen SelectionPen = new ImmutablePen(AccentBrush, 1.5);
	private static readonly IPen HandlePen = new ImmutablePen(AccentBrush, 1);
	private static readonly IPen HoverPen = new ImmutablePen(new ImmutableSolidColorBrush(AccentColor, 0.65), 1);
	private static readonly IPen ParentPen = new ImmutablePen(new ImmutableSolidColorBrush(AccentColor, 0.6), 1, Dashes);
	private static readonly IPen GridLinePen = new ImmutablePen(new ImmutableSolidColorBrush(AccentColor, 0.55), 1, Dashes);
	private static readonly IPen HintPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(128, 128, 128), 0.75), 1, Dashes);
	private static readonly IPen DropPen = new ImmutablePen(DropBrush, 2);
	private static readonly IPen DropLinePen = new ImmutablePen(DropBrush, 3);
	private static readonly IPen GhostPen = new ImmutablePen(AccentBrush, 1, Dashes);

	private void Paint(DrawingContext context)
	{
		if (_document is null || _preview is null || IsRunMode)
			return;
		var root = _document.Root;
		var selected = _document.Selected;
		bool dragging = _mode is DragMode.Move or DragMode.Tool;

		foreach (var node in _preview.EmptyContainers)
		{
			if (VisibleBoundsOf(node) is not { } rect || rect.Width < 12 || rect.Height < 12)
				continue;
			context.DrawRectangle(null, HintPen, Snap(rect).Deflate(1));
			string text = node == root ? "Drag a panel from the toolbox to here" : node.TypeName;
			DrawCentered(context, text, rect, HintTextBrush);
		}

		var gridNode = dragging && _dropTarget?.Container.TypeName == "Grid" ? _dropTarget.Container : GridFor(selected);
		if (gridNode is not null)
			PaintGridLines(context, gridNode);

		if (_mode == DragMode.None && _hover is not null && _hover != selected && VisibleBoundsOf(_hover) is { } hoverRect)
			context.DrawRectangle(null, HoverPen, Snap(hoverRect));

		if (selected is not null && BoundsOf(selected) is { } selectedRect)
		{
			if (selected.ObjectParent is { } parent && parent != root && BoundsOf(parent) is { } parentRect)
				context.DrawRectangle(null, ParentPen, Snap(parentRect));

			var frame = Snap(selectedRect);
			context.DrawRectangle(null, SelectionPen, frame);
			if (!dragging)
			{
				foreach (var (_, at) in HandlePoints(frame, selected == root))
				{
					var square = new Rect(at.X - HandleSize / 2, at.Y - HandleSize / 2, HandleSize, HandleSize);
					context.DrawRectangle(Brushes.White, HandlePen, square);
				}
			}

			string label = selected.Name is { } name ? $"{selected.ElementName}  {name}" : selected.ElementName;
			if (_mode is DragMode.Resize or DragMode.ResizeRoot)
			{
				var (width, height, _, _) = ResizeValues(_lastPoint);
				label = $"{(ResizesWidth ? width : _startSize.Width):0} × {(ResizesHeight ? height : _startSize.Height):0}";
			}
			else if (selected == root)
			{
				var size = GetDesignSize();
				label = $"{selected.ElementName}  {size.Width:0} × {size.Height:0}";
			}
			DrawTag(context, label, frame, AccentBrush, below: selected == root);
		}

		if (dragging)
		{
			if (_dropTarget is { } target)
			{
				context.DrawRectangle(null, DropPen, Snap(target.Bounds).Deflate(1));
				if (target.Highlight is { } highlight)
					context.DrawRectangle(DropFill, null, highlight.Intersect(target.Bounds));
				if (target.Line is { } line)
					context.DrawLine(DropLinePen, line.From, line.To);
				string text = target.Hint is null ? target.Container.ElementName : $"{target.Container.ElementName}: {target.Hint}";
				DrawTag(context, text, target.Bounds, DropBrush);
			}

			if (_mode == DragMode.Move)
			{
				var ghost = new Rect(_lastPoint - _grabOffset, _dragSize);
				context.DrawRectangle(GhostFill, GhostPen, ghost);
			}
			else if (_toolLabel is not null)
			{
				var text = MakeText(_toolLabel, Brushes.White);
				var box = new Rect(_lastPoint.X + 14, _lastPoint.Y + 14, text.Width + 12, text.Height + 6);
				context.DrawRectangle(_dropTarget is null ? HintTextBrush : AccentBrush, null, box, 4, 4);
				context.DrawText(text, new Point(box.X + 6, box.Y + 3));
			}
		}
	}

	private static DesignNode? GridFor(DesignNode? selected)
	{
		if (selected is null)
			return null;
		if (selected.TypeName == "Grid" && !selected.IsPropertyElement)
			return selected;
		return selected.ObjectParent is { TypeName: "Grid" } parent ? parent : null;
	}

	private void PaintGridLines(DrawingContext context, DesignNode gridNode)
	{
		if (ControlOf(gridNode) is not Grid grid || grid.TransformToVisual(Overlay) is not { } matrix)
			return;
		var (columns, rows) = GridTracks(grid);
		double width = grid.Bounds.Width;
		double height = grid.Bounds.Height;

		for (int i = 1; i < columns.Count; i++)
		{
			double x = (columns[i - 1].Start + columns[i - 1].Length + columns[i].Start) / 2;
			context.DrawLine(GridLinePen, new Point(x, 0).Transform(matrix), new Point(x, height).Transform(matrix));
		}
		for (int i = 1; i < rows.Count; i++)
		{
			double y = (rows[i - 1].Start + rows[i - 1].Length + rows[i].Start) / 2;
			context.DrawLine(GridLinePen, new Point(0, y).Transform(matrix), new Point(width, y).Transform(matrix));
		}

		string[] columnTexts = SplitDefinitions(gridNode.Get("ColumnDefinitions"));
		string[] rowTexts = SplitDefinitions(gridNode.Get("RowDefinitions"));
		for (int i = 0; i < columns.Count && columns.Count > 1; i++)
		{
			string label = i < columnTexts.Length ? columnTexts[i] : grid.ColumnDefinitions[i].Width.ToString();
			var text = MakeText(label, GridLabelBrush, 10);
			var center = new Point(columns[i].Start + columns[i].Length / 2, 0).Transform(matrix);
			if (text.Width < columns[i].Length * Zoom)
				context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height - 1));
		}
		for (int i = 0; i < rows.Count && rows.Count > 1; i++)
		{
			string label = i < rowTexts.Length ? rowTexts[i] : grid.RowDefinitions[i].Height.ToString();
			var text = MakeText(label, GridLabelBrush, 10);
			var center = new Point(0, rows[i].Start + rows[i].Length / 2).Transform(matrix);
			if (text.Height < rows[i].Length * Zoom)
				context.DrawText(text, new Point(center.X - text.Width - 3, center.Y - text.Height / 2));
		}
	}

	private static string[] SplitDefinitions(string? definitions) =>
		string.IsNullOrWhiteSpace(definitions)
			? Array.Empty<string>()
			: definitions.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

	private static FormattedText MakeText(string text, IBrush brush, double size = 11) =>
		new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

	private static Rect Snap(Rect rect) =>
		new(Math.Round(rect.X) + 0.5, Math.Round(rect.Y) + 0.5, Math.Max(0, Math.Round(rect.Width) - 1), Math.Max(0, Math.Round(rect.Height) - 1));

	private static void DrawCentered(DrawingContext context, string text, Rect rect, IBrush brush)
	{
		var formatted = MakeText(text, brush);
		if (formatted.Width + 8 > rect.Width || formatted.Height + 2 > rect.Height)
			return;
		context.DrawText(formatted, new Point(rect.Center.X - formatted.Width / 2, rect.Center.Y - formatted.Height / 2));
	}

	private static void DrawTag(DrawingContext context, string text, Rect rect, IBrush background, bool below = false)
	{
		var formatted = MakeText(text, Brushes.White);
		double height = formatted.Height + 4;
		double y = below ? rect.Bottom + 3 : rect.Top - height - 1;
		if (y < 0)
			y = rect.Top + 1;
		var box = new Rect(Math.Max(0, rect.Left), y, formatted.Width + 10, height);
		context.DrawRectangle(background, null, box, 3, 3);
		context.DrawText(formatted, new Point(box.X + 5, box.Y + 2));
	}
}
