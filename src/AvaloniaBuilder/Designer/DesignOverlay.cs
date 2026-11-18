using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace AvaloniaBuilder.Designer;

public sealed class DesignOverlay : Control, ICustomHitTest
{
	public Action<DrawingContext>? Painter { get; set; }

	public bool HitTest(Point point) => true;

	public override void Render(DrawingContext context)
	{
		Painter?.Invoke(context);
	}
}
