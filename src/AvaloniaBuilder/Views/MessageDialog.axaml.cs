using Avalonia.Controls;

namespace AvaloniaBuilder.Views;

public partial class MessageDialog : Window
{
	public MessageDialog()
	{
		InitializeComponent();
	}

	public static Task<string?> ShowAsync(Window owner, string title, string message, params string[] buttons)
	{
		var dialog = new MessageDialog { Title = title };
		dialog.MessageText.Text = message;
		if (buttons.Length == 0)
			buttons = new[] { "OK" };

		for (int i = 0; i < buttons.Length; i++)
		{
			string text = buttons[i];
			var button = new Button { Content = text, MinWidth = 86, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
			if (i == 0)
			{
				button.Classes.Add("accent");
				button.IsDefault = true;
			}
			if (i == buttons.Length - 1 && buttons.Length > 1)
				button.IsCancel = true;
			button.Click += (_, _) => dialog.Close(text);
			dialog.ButtonRow.Children.Add(button);
		}
		return dialog.ShowDialog<string?>(owner);
	}
}
