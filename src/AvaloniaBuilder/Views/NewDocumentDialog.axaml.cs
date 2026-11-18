using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Services;

namespace AvaloniaBuilder.Views;

public partial class NewDocumentDialog : Window
{
	public NewDocumentDialog() : this("MyApp")
	{
	}

	public NewDocumentDialog(string defaultNamespace)
	{
		InitializeComponent();
		TemplateList.ItemsSource = DocumentTemplates.All;
		NamespaceBox.Text = defaultNamespace;
		TemplateList.SelectedIndex = 0;
		Update();
	}

	private DocumentTemplate? SelectedTemplate => TemplateList.SelectedItem as DocumentTemplate;

	private void OnTemplateChanged(object? sender, SelectionChangedEventArgs e)
	{
		string current = ViewNameBox.Text?.Trim() ?? "";
		if (SelectedTemplate is { } template && current is "" or "MainWindow" or "MainView")
			ViewNameBox.Text = template.IsWindow ? "MainWindow" : "MainView";
		Update();
	}

	private void OnNamesChanged(object? sender, TextChangedEventArgs e) => Update();

	private void Update()
	{
		if (SummaryText is null || CreateButton is null)
			return;
		string ns = NamespaceBox.Text?.Trim() ?? "";
		string view = ViewNameBox.Text?.Trim() ?? "";
		bool valid = CodeNames.IsQualifiedName(ns) && CodeNames.IsIdentifier(view) && SelectedTemplate is not null;
		CreateButton.IsEnabled = valid;
		SummaryText.Text = valid
			? $"View class: {ns}.Views.{view}\nView model: {ViewModelGenerator.SuggestViewModelClass(ns + ".Views." + view)}"
			: "Use letters, digits and _ only. A name cannot start with a digit.";
	}

	private void OnCreate(object? sender, RoutedEventArgs e)
	{
		if (!CreateButton.IsEnabled || SelectedTemplate is not { } template)
			return;
		Close(new NewDocumentChoice(template, NamespaceBox.Text!.Trim(), ViewNameBox.Text!.Trim()));
	}

	private void OnTemplateDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => OnCreate(sender, e);

	private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
