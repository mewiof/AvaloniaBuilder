using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AvaloniaBuilder.Editors;

public class CommitTextBox : TextBox
{
	public static readonly StyledProperty<string?> CommittedTextProperty =
		AvaloniaProperty.Register<CommitTextBox, string?>(nameof(CommittedText), defaultBindingMode: BindingMode.TwoWay);

	public static readonly StyledProperty<bool> LiveCommitProperty =
		AvaloniaProperty.Register<CommitTextBox, bool>(nameof(LiveCommit));

	private bool _updating;

	protected override Type StyleKeyOverride => typeof(TextBox);

	public string? CommittedText
	{
		get => GetValue(CommittedTextProperty);
		set => SetValue(CommittedTextProperty, value);
	}

	public bool LiveCommit
	{
		get => GetValue(LiveCommitProperty);
		set => SetValue(LiveCommitProperty, value);
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (_updating)
			return;

		if (change.Property == CommittedTextProperty)
		{
			string text = CommittedText ?? "";
			if ((Text ?? "") != text)
			{
				_updating = true;
				Text = text;
				_updating = false;
			}
		}
		else if (change.Property == TextProperty && LiveCommit && IsFocused)
		{
			Commit();
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Enter && !AcceptsReturn)
		{
			Commit();
			SelectAll();
			e.Handled = true;
			return;
		}
		if (e.Key == Key.Escape)
		{
			_updating = true;
			Text = CommittedText ?? "";
			_updating = false;
			SelectAll();
			e.Handled = true;
			return;
		}
		base.OnKeyDown(e);
	}

	protected override void OnLostFocus(FocusChangedEventArgs e)
	{
		base.OnLostFocus(e);
		Commit();
	}

	private void Commit()
	{
		string text = Text ?? "";
		if ((CommittedText ?? "") == text)
			return;
		_updating = true;
		SetCurrentValue(CommittedTextProperty, text);
		_updating = false;
	}
}
