using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaBuilder.Editors;

namespace AvaloniaBuilder.Views;

public partial class PropertiesView : UserControl
{
	public PropertiesView()
	{
		InitializeComponent();
	}

	public void FocusMainEditor()
	{
		Dispatcher.UIThread.Post(() =>
		{
			var box = GroupList.GetVisualDescendants().OfType<CommitTextBox>().FirstOrDefault(b => b.IsEffectivelyVisible);
			if (box is null)
				return;
			box.Focus();
			box.SelectAll();
		}, DispatcherPriority.Loaded);
	}
}
