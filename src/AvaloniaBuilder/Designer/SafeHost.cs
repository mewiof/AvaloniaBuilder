using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AvaloniaBuilder.Designer;

public sealed class SafeHost : Decorator
{
	public event EventHandler<string>? LayoutFailed;

	protected override Size MeasureOverride(Size availableSize)
	{
		try
		{
			return base.MeasureOverride(availableSize);
		}
		catch (Exception ex)
		{
			Fail(ex);
			return default;
		}
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		try
		{
			return base.ArrangeOverride(finalSize);
		}
		catch (Exception ex)
		{
			Fail(ex);
			return finalSize;
		}
	}

	private void Fail(Exception ex)
	{
		string message = ex.Message;
		Dispatcher.UIThread.Post(() =>
		{
			Child = null;
			LayoutFailed?.Invoke(this, message);
		});
	}
}
