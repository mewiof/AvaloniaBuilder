using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaBuilder.ViewModels;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;

namespace AvaloniaBuilder.Views;

public partial class CodeView : UserControl
{
	private readonly DispatcherTimer _applyTimer;
	private readonly LineHighlighter _highlighter = new();
	private MainViewModel? _viewModel;

	private bool _settingText;

	private bool _textPending;

	public CodeView()
	{
		InitializeComponent();

		XamlEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("XML");
		CodeBehindEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
		ViewModelEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C#");
		foreach (var editor in new[] { XamlEditor, CodeBehindEditor, ViewModelEditor })
		{
			editor.Options.ConvertTabsToSpaces = true;
			editor.Options.IndentationSize = 4;
			editor.Options.EnableHyperlinks = false;
			editor.Options.EnableEmailHyperlinks = false;
		}

		XamlEditor.TextArea.TextView.BackgroundRenderers.Add(_highlighter);
		XamlEditor.TextChanged += OnXamlTextChanged;
		XamlEditor.TextArea.Caret.PositionChanged += OnCaretMoved;
		XamlEditor.TextArea.LostFocus += (_, _) => ApplyNow();

		_applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
		_applyTimer.Tick += (_, _) => ApplyNow();

		ActualThemeVariantChanged += (_, _) => ApplyColors();
		ApplyColors();
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);
		if (_viewModel is not null)
			_viewModel.PropertyChanged -= OnViewModelChanged;
		_viewModel = DataContext as MainViewModel;
		if (_viewModel is null)
			return;
		_viewModel.PropertyChanged += OnViewModelChanged;
		SetText(XamlEditor, _viewModel.XamlText);
		SetText(CodeBehindEditor, _viewModel.CodeBehindText);
		SetText(ViewModelEditor, _viewModel.ViewModelText);
		UpdateHighlight(scroll: false);
	}

	private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (_viewModel is null)
			return;
		switch (e.PropertyName)
		{
			case nameof(MainViewModel.XamlText):
				_applyTimer.Stop();
				_textPending = false;
				SetText(XamlEditor, _viewModel.XamlText);
				break;
			case nameof(MainViewModel.CodeBehindText):
				SetText(CodeBehindEditor, _viewModel.CodeBehindText);
				break;
			case nameof(MainViewModel.ViewModelText):
				SetText(ViewModelEditor, _viewModel.ViewModelText);
				break;
			case nameof(MainViewModel.SelectedLine):
			case nameof(MainViewModel.SelectedEndLine):
				UpdateHighlight(scroll: !XamlEditor.TextArea.IsKeyboardFocusWithin);
				break;
		}
	}

	private void SetText(TextEditor editor, string text)
	{
		if (editor.Text == text)
			return;
		_settingText = true;
		try
		{
			int caret = editor.CaretOffset;
			double scroll = editor.VerticalOffset;
			editor.Document.Text = text;
			editor.Document.UndoStack.ClearAll();
			editor.CaretOffset = Math.Min(caret, text.Length);
			editor.ScrollToVerticalOffset(scroll);
		}
		finally
		{
			_settingText = false;
		}
	}

	private void OnXamlTextChanged(object? sender, EventArgs e)
	{
		if (_settingText)
			return;
		_textPending = true;
		_applyTimer.Stop();
		_applyTimer.Start();
	}

	public void ApplyNow()
	{
		_applyTimer.Stop();
		if (!_textPending || _viewModel is null)
			return;
		_textPending = false;
		_viewModel.ApplyXamlText(XamlEditor.Text);
		UpdateHighlight(scroll: false);
	}

	private void OnCaretMoved(object? sender, EventArgs e)
	{
		if (_settingText || _textPending || _viewModel is null || !XamlEditor.TextArea.IsKeyboardFocusWithin)
			return;
		_viewModel.SelectNodeAtLine(XamlEditor.TextArea.Caret.Line);
	}

	private void UpdateHighlight(bool scroll)
	{
		if (_viewModel is null)
			return;
		bool isRoot = _viewModel.SelectedNode?.Parent is null;
		_highlighter.Start = isRoot ? 0 : _viewModel.SelectedLine;
		_highlighter.End = isRoot ? 0 : _viewModel.SelectedEndLine;
		XamlEditor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
		if (scroll && _viewModel.SelectedLine > 0 && _viewModel.SelectedLine <= XamlEditor.Document.LineCount)
			XamlEditor.ScrollTo(_viewModel.SelectedLine, 0);
	}

	private void OnProblemSelected(object? sender, SelectionChangedEventArgs e)
	{
		if (ProblemList.SelectedItem is ProblemViewModel problem)
			_viewModel?.SelectProblemCommand.Execute(problem);
	}

	public void ShowProblems() => Tabs.SelectedIndex = 3;

	private void ApplyColors()
	{
		bool dark = ActualThemeVariant == ThemeVariant.Dark;
		if (this.TryFindResource("LineHighlightBrush", ActualThemeVariant, out var found) && found is IBrush brush)
			_highlighter.Brush = brush;

		string keyword = dark ? "#569CD6" : "#0000FF";
		string text = dark ? "#CE9178" : "#A31515";
		string comment = dark ? "#6A9955" : "#008000";

		var xml = HighlightingManager.Instance.GetDefinition("XML");
		SetColor(xml, "Comment", comment);
		SetColor(xml, "CData", dark ? "#D7BA7D" : "#808080");
		SetColor(xml, "DocType", keyword);
		SetColor(xml, "XmlDeclaration", keyword);
		SetColor(xml, "XmlTag", dark ? "#569CD6" : "#800000");
		SetColor(xml, "AttributeName", dark ? "#9CDCFE" : "#C00000");
		SetColor(xml, "AttributeValue", dark ? "#CE9178" : "#0000FF");
		SetColor(xml, "Entity", dark ? "#D7BA7D" : "#008080");
		SetColor(xml, "BrokenEntity", dark ? "#F44747" : "#808000");

		var csharp = HighlightingManager.Instance.GetDefinition("C#");
		foreach (var color in csharp.NamedHighlightingColors)
		{
			string value = color.Name switch
			{
				"Comment" => comment,
				"String" or "Char" => text,
				"StringInterpolation" or "Punctuation" => dark ? "#D4D4D4" : "#000000",
				"Preprocessor" => dark ? "#9B9B9B" : "#808080",
				"MethodCall" => dark ? "#DCDCAA" : "#74531F",
				"NumberLiteral" => dark ? "#B5CEA8" : "#098658",
				_ => keyword,
			};
			SetColor(csharp, color.Name, value);
		}

		foreach (var editor in new[] { XamlEditor, CodeBehindEditor, ViewModelEditor })
			editor.TextArea.TextView.Redraw();
	}

	private static void SetColor(IHighlightingDefinition definition, string name, string color)
	{
		try
		{
			if (definition.GetNamedColor(name) is { } named)
			{
				named.Foreground = new SimpleHighlightingBrush(Color.Parse(color));
				named.FontWeight = null;
			}
		}
		catch (InvalidOperationException)
		{
		}
	}

	private sealed class LineHighlighter : IBackgroundRenderer
	{
		public int Start { get; set; }

		public int End { get; set; }

		public IBrush? Brush { get; set; }

		public KnownLayer Layer => KnownLayer.Background;

		public void Draw(TextView textView, DrawingContext drawingContext)
		{
			if (Start <= 0 || Brush is null || !textView.VisualLinesValid)
				return;
			foreach (var line in textView.VisualLines)
			{
				int number = line.FirstDocumentLine.LineNumber;
				if (number < Start || number > End)
					continue;
				double y = line.VisualTop - textView.VerticalOffset;
				drawingContext.FillRectangle(Brush, new Rect(0, y, textView.Bounds.Width, line.Height));
			}
		}
	}
}
