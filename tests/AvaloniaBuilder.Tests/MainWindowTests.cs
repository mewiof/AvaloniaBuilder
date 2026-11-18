using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaBuilder.Designer;
using AvaloniaBuilder.Editors;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Services;
using AvaloniaBuilder.ViewModels;
using AvaloniaBuilder.Views;

namespace AvaloniaBuilder.Tests;

public class MainWindowTests
{
	private static (MainWindow Window, MainViewModel Vm, FakeDialogs Dialogs) Open(int templateIndex = 0, double width = 1500, double height = 920)
	{
		var dialogs = new FakeDialogs();
		var vm = new MainViewModel(dialogs, new AppSettings());
		if (templateIndex != 0)
			vm.SetDocument(DocumentTemplates.Create(DocumentTemplates.All[templateIndex]));
		var window = new MainWindow(vm) { Width = width, Height = height };
		window.Show();
		TestHelpers.Settle();
		return (window, vm, dialogs);
	}

	private static DesignSurface SurfaceOf(Window window) => window.GetVisualDescendants().OfType<DesignSurface>().Single();

	private static Point Middle(Window window, DesignNode node, double fx = 0.5, double fy = 0.5)
	{
		var surface = SurfaceOf(window);
		var overlay = surface.GetVisualDescendants().OfType<DesignOverlay>().Single();
		var rect = surface.BoundsOf(node)!.Value;
		return overlay.TranslatePoint(new Point(rect.X + rect.Width * fx, rect.Y + rect.Height * fy), window)!.Value;
	}

	private static void Click(Window window, Point point)
	{
		window.MouseMove(point);
		window.MouseDown(point, MouseButton.Left);
		window.MouseUp(point, MouseButton.Left);
		TestHelpers.Settle();
	}

	[AvaloniaFact]
	public void The_window_opens_with_an_empty_design()
	{
		var (window, vm, _) = Open();

		Assert.Equal("Untitled.axaml - Avalonia Builder", window.Title);
		Assert.Equal("Window", vm.Document.Root.TypeName);
		Assert.Contains("<Window xmlns=\"https://github.com/avaloniaui\"", vm.XamlText);
		Assert.Contains("public partial class MainWindow : Window", vm.CodeBehindText);
		Assert.Contains("public partial class MainViewModel : ObservableObject", vm.ViewModelText);
		Assert.Equal("No problems", vm.ProblemSummary);
		window.SaveScreen("main-empty");
		window.Close();
	}

	[AvaloniaFact]
	public void The_control_panel_template_looks_right_in_light_and_dark()
	{
		var (window, vm, _) = Open(templateIndex: 1);
		var slider = vm.Document.Root.Descendants().First(n => n.TypeName == "Slider");

		Click(window, Middle(window, slider));
		Assert.Same(slider, vm.SelectedNode);
		Assert.Equal("Slider", vm.Properties.Header);
		Assert.Contains("Slider", vm.SelectionPath);
		window.SaveScreen("main-control-panel-light");

		Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
		TestHelpers.Settle();
		window.SaveScreen("main-control-panel-dark");
		Application.Current.RequestedThemeVariant = ThemeVariant.Default;
		window.Close();
	}

	[AvaloniaFact]
	public void Dragging_from_the_toolbox_adds_the_control_and_all_panels_follow()
	{
		var (window, vm, _) = Open();
		var grid = vm.Document.Root.ContentChildren.Single();
		var toolbox = window.GetVisualDescendants().OfType<ToolboxView>().Single();
		vm.Toolbox.Filter = "Button";
		TestHelpers.Settle();
		var entry = toolbox.GetVisualDescendants().OfType<Border>()
			.First(b => b.Classes.Contains("tool") && b.DataContext is ToolboxItem { Name: "Button" });

		var from = entry.CenterOf(window);
		var to = Middle(window, grid);
		window.MouseMove(from);
		window.MouseDown(from, MouseButton.Left);
		for (int i = 1; i <= 10; i++)
			window.MouseMove(from + (to - from) * (i / 10.0));
		window.SaveScreen("main-drag-from-toolbox");
		window.MouseUp(to, MouseButton.Left);
		TestHelpers.Settle();

		var button = Assert.Single(grid.Children);
		Assert.Equal("Button", button.TypeName);
		Assert.Same(button, vm.SelectedNode);
		Assert.Contains("<Button Content=\"Button\" />", vm.XamlText);
		Assert.Contains(vm.Outline.Rows, row => row.Node == button);
		Assert.Equal("Button", vm.Properties.Header);
		Assert.EndsWith("* - Avalonia Builder", window.Title);
		window.SaveScreen("main-after-drop");
		window.Close();
	}

	[AvaloniaFact]
	public void Typing_in_the_property_panel_changes_the_design_and_the_code()
	{
		var (window, vm, _) = Open();
		vm.AddFromToolboxCommand.Execute(ToolboxCatalog.Items.First(i => i.Name == "Button"));
		TestHelpers.Settle();
		var button = vm.SelectedNode!;

		var content = vm.Properties.Groups.First(g => g.Name == "Common").Items.First(i => i.Name == "Content");
		var box = window.GetVisualDescendants().OfType<CommitTextBox>().First(b => ReferenceEquals(b.DataContext, content) && b.IsEffectivelyVisible);
		box.Focus();
		box.SelectAll();
		window.KeyTextInput("Start");
		TestHelpers.Settle();

		Assert.Equal("Start", button.Get("Content"));
		Assert.Contains("Content=\"Start\"", vm.XamlText);
		Assert.Equal("Start", ((Button)SurfaceOf(window).Preview!.GetControl(button)!).Content);
		Assert.True(box.IsFocused);

		var command = vm.Properties.Groups.SelectMany(g => g.Items).First(i => i.Name == "Command");
		command.Text = "StartCommand";
		TestHelpers.Settle();
		Assert.Equal("{Binding StartCommand}", button.Get("Command"));
		Assert.Contains("[RelayCommand]\n    private void Start()", vm.ViewModelText);

		var margin = vm.Properties.Groups.SelectMany(g => g.Items).First(i => i.Name == "Margin");
		margin.Text = "abc";
		Assert.True(margin.HasError);
		Assert.Null(button.Get("Margin"));
		margin.Text = "8,4";
		Assert.False(margin.HasError);
		Assert.Equal("8,4", button.Get("Margin"));

		window.SaveScreen("main-properties");
		window.Close();
	}

	[AvaloniaFact]
	public void Delete_undo_copy_and_paste_work_with_the_keyboard()
	{
		var (window, vm, dialogs) = Open(templateIndex: 1);
		var slider = vm.Document.Root.Descendants().First(n => n.TypeName == "Slider");
		var parent = slider.Parent!;
		int count = parent.Children.Count;
		Click(window, Middle(window, slider));

		window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
		TestHelpers.Settle();
		Assert.Contains("<Slider", dialogs.Clipboard);
		Assert.Contains("xmlns=\"https://github.com/avaloniaui\"", dialogs.Clipboard);

		window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
		TestHelpers.Settle();
		Assert.Equal(count - 1, parent.Children.Count);

		window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
		TestHelpers.Settle();
		Assert.Equal(count, vm.Document.Root.Descendants().First(n => n.TypeName == "Slider").Parent!.Children.Count);

		window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
		TestHelpers.Settle();
		Assert.Equal(2, vm.Document.Root.Descendants().Count(n => n.TypeName == "Slider"));
		window.Close();
	}

	[AvaloniaFact]
	public void Text_typed_in_the_xaml_tab_becomes_the_design()
	{
		var (window, vm, _) = Open();

		string edited = vm.XamlText.Replace("<Grid Margin=\"16\" />", "<Grid Margin=\"16\">\n        <TextBlock Text=\"Typed by hand\" />\n    </Grid>");
		vm.ApplyXamlText(edited);
		TestHelpers.Settle();

		var text = vm.Document.Root.Descendants().Single(n => n.TypeName == "TextBlock");
		Assert.Equal("Typed by hand", text.Get("Text"));
		Assert.Null(vm.XamlError);
		Assert.Equal(edited, vm.XamlText);

		vm.ApplyXamlText(edited.Replace("</Grid>", ""));
		Assert.NotNull(vm.XamlError);
		Assert.Contains("Line", vm.XamlError);
		Assert.Single(vm.Document.Root.Descendants(), n => n.TypeName == "TextBlock");

		vm.UndoCommand.Execute(null);
		Assert.DoesNotContain(vm.Document.Root.Descendants(), n => n.TypeName == "TextBlock");
		window.Close();
	}

	[AvaloniaFact]
	public async Task Save_and_open_keep_the_document()
	{
		var (window, vm, dialogs) = Open(templateIndex: 3);
		string path = Path.Combine(Path.GetTempPath(), "AvaloniaBuilderTests", Guid.NewGuid().ToString("N"), "FormView.axaml");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		try
		{
			dialogs.SavePath = path;
			vm.Document.SetAttribute(vm.Document.Root, "Title", "Changed");
			Assert.True(vm.Document.IsDirty);

			await vm.SaveCommand.ExecuteAsync(null);
			Assert.False(vm.Document.IsDirty);
			Assert.Equal("FormView.axaml - Avalonia Builder", vm.Title);
			string saved = File.ReadAllText(path);
			Assert.Contains("Title=\"Changed\"", saved);

			vm.SetDocument(DocumentTemplates.Create(DocumentTemplates.All[0]));
			dialogs.OpenPath = path;
			await vm.OpenCommand.ExecuteAsync(null);
			Assert.Equal("Changed", vm.Document.Root.Get("Title"));
			Assert.Equal(path, vm.Document.FilePath);
			Assert.Equal(path, vm.RecentFiles[0]);
			Assert.Equal(saved.ReplaceLineEndings("\n"), vm.XamlText);
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
			window.Close();
		}
	}

	[AvaloniaFact]
	public async Task Unsaved_changes_ask_before_a_new_document()
	{
		var (window, vm, dialogs) = Open();
		vm.Document.SetAttribute(vm.Document.Root, "Title", "Changed");
		dialogs.NewChoice = new NewDocumentChoice(DocumentTemplates.All[5], "Robot", "JointView");

		dialogs.Answer = "Cancel";
		await vm.NewCommand.ExecuteAsync(null);
		Assert.Equal("Window", vm.Document.Root.TypeName);
		Assert.Single(dialogs.Questions);

		dialogs.Answer = "Don't save";
		await vm.NewCommand.ExecuteAsync(null);
		Assert.Equal("UserControl", vm.Document.Root.TypeName);
		Assert.Equal("Robot.Views.JointView", vm.Document.ViewClass);
		Assert.Equal("Robot.ViewModels.JointViewModel", vm.Document.ViewModelClass);
		window.Close();
	}

	[AvaloniaFact]
	public async Task Export_writes_the_files()
	{
		var (window, vm, dialogs) = Open(templateIndex: 1);
		string folder = Path.Combine(Path.GetTempPath(), "AvaloniaBuilderTests", Guid.NewGuid().ToString("N"));
		try
		{
			dialogs.ExportChoice = new ExportChoice(folder, CreateProject: false, Overwrite: false);
			dialogs.Answer = "Close";

			await vm.ExportCommand.ExecuteAsync(null);

			Assert.True(File.Exists(Path.Combine(folder, "Views", "MainWindow.axaml")));
			Assert.True(File.Exists(Path.Combine(folder, "Views", "MainWindow.axaml.cs")));
			Assert.Contains("private void Stop()", File.ReadAllText(Path.Combine(folder, "ViewModels", "MainViewModel.cs")));
			Assert.Contains("3 files written", dialogs.Questions.Single());
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
			window.Close();
		}
	}

	[AvaloniaFact]
	public void The_outline_selects_and_moves_elements()
	{
		var (window, vm, _) = Open(templateIndex: 1);
		var checkBox = vm.Document.Root.Descendants().First(n => n.TypeName == "CheckBox");
		var slider = vm.Document.Root.Descendants().First(n => n.TypeName == "Slider");

		vm.Outline.SelectedRow = vm.Outline.Rows.First(r => r.Node == checkBox);
		Assert.Same(checkBox, vm.SelectedNode);

		Assert.True(vm.Outline.CanDrop(checkBox, slider, DropPlace.Before));
		Assert.False(vm.Outline.CanDrop(checkBox, slider, DropPlace.Inside));
		vm.Outline.Drop(checkBox, slider, DropPlace.Before);
		Assert.Equal(slider.Index - 1, checkBox.Index);
		Assert.Same(slider.Parent, checkBox.Parent);

		var panelRow = vm.Outline.Rows.First(r => r.Node == slider.Parent);
		vm.Outline.ToggleCommand.Execute(panelRow);
		Assert.DoesNotContain(vm.Outline.Rows, r => r.Node == slider);
		window.Close();
	}

	[AvaloniaFact]
	public void Wrap_and_unwrap()
	{
		var (window, vm, _) = Open();
		vm.AddFromToolboxCommand.Execute(ToolboxCatalog.Items.First(i => i.Name == "Button"));
		var button = vm.SelectedNode!;
		vm.Document.SetAttribute(button, "Grid.Row", "1");

		vm.WrapCommand.Execute("Border");
		var border = vm.SelectedNode!;
		Assert.Equal("Border", border.TypeName);
		Assert.Same(border, button.Parent);
		Assert.Equal("1", border.Get("Grid.Row"));
		Assert.Null(button.Get("Grid.Row"));

		Assert.True(vm.UnwrapCommand.CanExecute(null));
		vm.UnwrapCommand.Execute(null);
		Assert.Equal("Grid", button.Parent!.TypeName);
		Assert.Equal("1", button.Get("Grid.Row"));
		window.Close();
	}

	[AvaloniaFact]
	public void A_grid_gets_rows_and_columns_from_the_menu()
	{
		var (window, vm, _) = Open();
		var grid = vm.Document.Root.ContentChildren.Single();
		vm.Document.Select(grid);

		vm.AddGridRowCommand.Execute(null);
		vm.AddGridColumnCommand.Execute(null);
		vm.AddGridColumnCommand.Execute(null);
		Assert.Equal("*,*", grid.Get("RowDefinitions"));
		Assert.Equal("*,*,*", grid.Get("ColumnDefinitions"));

		vm.AddFromToolboxCommand.Execute(ToolboxCatalog.Items.First(i => i.Name == "Button"));
		Assert.True(vm.AddGridRowCommand.CanExecute(null));
		vm.AddGridRowCommand.Execute(null);
		Assert.Equal("*,*,*", grid.Get("RowDefinitions"));

		vm.Document.Select(vm.Document.Root);
		Assert.False(vm.AddGridRowCommand.CanExecute(null));
		window.Close();
	}

	[AvaloniaFact]
	public void Typing_another_type_name_changes_the_element()
	{
		var (window, vm, _) = Open();
		vm.AddFromToolboxCommand.Execute(ToolboxCatalog.Items.First(i => i.Name == "Your own control"));
		var node = vm.SelectedNode!;
		Assert.Equal("local:MyControl", node.ElementName);
		Assert.Equal("using:MyApp", vm.Document.GetNamespaceUri("local"));

		vm.Properties.ElementType = "ctl:ArmView";
		Assert.Equal("ctl:ArmView", node.ElementName);
		Assert.Equal("using:MyApp.Controls", vm.Document.GetNamespaceUri("ctl"));
		Assert.Contains("<ctl:ArmView />", vm.XamlText);
		Assert.Equal("ctl:ArmView", vm.Properties.Header);

		vm.Properties.ElementType = "9 bad";
		Assert.NotNull(vm.Properties.ElementTypeError);
		Assert.Equal("ctl:ArmView", node.ElementName);

		var grid = vm.Document.Root.ContentChildren.Single();
		vm.Document.Select(grid);
		vm.Properties.ElementType = "DockPanel";
		Assert.Equal("DockPanel", grid.TypeName);
		Assert.Same(grid, node.Parent);
		TestHelpers.Settle();
		Assert.False(vm.HasProblems);
		window.Close();
	}

	[AvaloniaFact]
	public void The_delete_key_only_works_in_the_design_and_in_the_outline()
	{
		var (window, vm, _) = Open();
		vm.AddFromToolboxCommand.Execute(ToolboxCatalog.Items.First(i => i.Name == "Button"));
		TestHelpers.Settle();
		var button = vm.SelectedNode!;

		var combo = window.GetVisualDescendants().OfType<PropertiesView>().Single()
			.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsEffectivelyVisible);
		combo.Focus();
		window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
		TestHelpers.Settle();
		Assert.NotNull(button.Parent);

		Click(window, Middle(window, button));
		window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
		TestHelpers.Settle();
		Assert.Null(button.Parent);
		window.Close();
	}

	[AvaloniaFact]
	public void A_double_click_in_the_toolbox_adds_the_control_and_a_double_click_in_the_design_edits_it()
	{
		var (window, vm, _) = Open();
		var toolbox = window.GetVisualDescendants().OfType<ToolboxView>().Single();
		vm.Toolbox.Filter = "CheckBox";
		TestHelpers.Settle();
		var entry = toolbox.GetVisualDescendants().OfType<Border>()
			.First(b => b.Classes.Contains("tool") && b.DataContext is ToolboxItem { Name: "CheckBox" });

		var point = entry.CenterOf(window);
		Click(window, point);
		Click(window, point);
		var checkBox = Assert.Single(vm.Document.Root.Descendants(), n => n.TypeName == "CheckBox");
		Assert.Same(checkBox, vm.SelectedNode);

		var inDesign = Middle(window, checkBox);
		Click(window, inDesign);
		Click(window, inDesign);
		TestHelpers.Settle();
		var focused = window.FocusManager!.GetFocusedElement();
		var box = Assert.IsType<CommitTextBox>(focused);
		Assert.Equal("Content", ((PropertyItemViewModel)box.DataContext!).Name);

		window.KeyTextInput("Remember me");
		TestHelpers.Settle();
		Assert.Equal("Remember me", checkBox.Get("Content"));
		window.Close();
	}

	[AvaloniaFact]
	public void Shift_drops_into_the_panel_one_level_higher()
	{
		var (window, vm, _) = Open();
		vm.ApplyXamlText(vm.XamlText.Replace("<Grid Margin=\"16\" />",
			"<StackPanel><Border Height=\"200\"><StackPanel x:Name=\"Inner\" /></Border><Button Content=\"Move me\" /></StackPanel>"));
		TestHelpers.Settle();
		var outer = vm.Document.Root.ContentChildren.Single();
		var inner = outer.Descendants().First(n => n.Name == "Inner");
		var button = outer.Children[1];

		var from = Middle(window, button);
		var to = Middle(window, inner);
		window.MouseMove(from);
		window.MouseDown(from, MouseButton.Left);
		window.MouseMove(from + (to - from) / 2);
		window.MouseMove(to);
		window.MouseUp(to, MouseButton.Left);
		TestHelpers.Settle();
		Assert.Same(inner, button.Parent);

		vm.UndoCommand.Execute(null);
		TestHelpers.Settle();
		outer = vm.Document.Root.ContentChildren.Single();
		inner = outer.Descendants().First(n => n.Name == "Inner");
		button = outer.Children[1];
		from = Middle(window, button);
		to = Middle(window, inner);
		window.MouseMove(from);
		window.MouseDown(from, MouseButton.Left);
		window.MouseMove(from + (to - from) / 2, RawInputModifiers.Shift);
		window.MouseMove(to, RawInputModifiers.Shift);
		window.MouseUp(to, MouseButton.Left, RawInputModifiers.Shift);
		TestHelpers.Settle();
		Assert.Same(outer, button.Parent);
		window.Close();
	}

	[AvaloniaFact]
	public void The_wheel_with_ctrl_zooms_and_any_attribute_can_be_added_by_name()
	{
		var (window, vm, _) = Open();
		var grid = vm.Document.Root.ContentChildren.Single();
		double before = vm.Zoom;

		window.MouseWheel(Middle(window, grid), new Vector(0, 1), RawInputModifiers.Control);
		TestHelpers.Settle();
		Assert.True(vm.Zoom > before);
		vm.ZoomResetCommand.Execute(null);
		Assert.Equal(1, vm.Zoom);
		Assert.Equal("100 %", vm.ZoomText);

		vm.Document.Select(grid);
		vm.Properties.NewAttributeName = "ctl:Dock.Side";
		vm.Properties.NewAttributeValue = "Left";
		vm.Properties.AddAttributeCommand.Execute(null);
		Assert.Equal("Left", grid.Get("ctl:Dock.Side"));
		Assert.Contains(vm.Properties.Groups.Single(g => g.Name == PropertyGridViewModel.AttributesGroup).Items, i => i.Name == "ctl:Dock.Side");
		window.Close();
	}

	[AvaloniaFact]
	public void The_class_names_of_the_document_can_be_changed_and_are_checked()
	{
		var (window, vm, _) = Open();
		var rows = vm.Properties.Groups.Single(g => g.Name == PropertyGridViewModel.DocumentGroup).Items;
		var viewClass = rows.Single(i => i.Name == "x:Class");
		var viewModel = rows.Single(i => i.Name == "View model");
		Assert.Equal("MyApp.Views.MainWindow", viewClass.Text);

		viewClass.Text = "Robot.Views.PanelWindow";
		viewModel.Text = "Robot.ViewModels.PanelViewModel";
		Assert.Equal("Robot.Views.PanelWindow", vm.Document.ViewClass);
		Assert.Equal("Robot.ViewModels.PanelViewModel", vm.Document.ViewModelClass);
		Assert.Contains("namespace Robot.Views;", vm.CodeBehindText);
		Assert.Contains("public partial class PanelWindow : Window", vm.CodeBehindText);
		Assert.Contains("public partial class PanelViewModel", vm.ViewModelText);
		Assert.Contains("using:Robot.ViewModels", vm.XamlText);

		viewClass.Text = "not a name";
		Assert.True(viewClass.HasError);
		Assert.Equal("Robot.Views.PanelWindow", vm.Document.ViewClass);
		window.Close();
	}

	[AvaloniaFact]
	public void A_mistake_in_the_xaml_shows_as_a_problem()
	{
		var (window, vm, _) = Open();
		string broken = vm.XamlText.Replace("<Grid Margin=\"16\" />", "<Grid Margin=\"16\"><Button Colour=\"Red\" /></Grid>");

		vm.ApplyXamlText(broken);
		TestHelpers.Settle();

		Assert.True(vm.HasProblems);
		Assert.Equal("1 problem", vm.ProblemSummary);
		Assert.Contains("no property 'Colour'", vm.Problems[0].Message);
		Assert.True(vm.Outline.Rows.Single(r => r.Node.TypeName == "Button").HasProblem);
		window.SaveScreen("main-problem");
		window.Close();
	}
}
