using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Designer;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Services;
using AvaloniaBuilder.ViewModels;
using AvaloniaBuilder.Views;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Tests;

public class MoreUiTests
{
	private static (MainWindow Window, MainViewModel Vm, FakeDialogs Dialogs) Open(DesignDocument? document = null)
	{
		var dialogs = new FakeDialogs();
		var vm = new MainViewModel(dialogs, new AppSettings());
		if (document is not null)
			vm.SetDocument(document);
		var window = new MainWindow(vm) { Width = 1500, Height = 920 };
		window.Show();
		TestHelpers.Settle();
		return (window, vm, dialogs);
	}

	private static DesignDocument RobotDocument() =>
		DesignXamlReader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "RobotMainWindow.axaml.txt")));

	[AvaloniaFact]
	public void A_big_real_file_opens_and_shows_its_own_controls_as_gray_boxes()
	{
		var (window, vm, _) = Open(RobotDocument());

		Assert.False(vm.HasProblems);
		var arm = vm.Document.Root.Descendants().First(n => n.ElementName == "ctl:ArmView");
		vm.Document.Select(arm);
		TestHelpers.Settle();

		Assert.Equal("ctl:ArmView", vm.Properties.Header);
		Assert.Contains(vm.Properties.Groups, g => g.Name == "Layout");
		Assert.Contains(vm.Properties.Groups.Single(g => g.Name == PropertyGridViewModel.AttributesGroup).Items, i => i.Name == "ArmBrush");
		Assert.Contains("class MainViewModel", vm.ViewModelText);
		Assert.Contains("public ObservableCollection<JointViewModel> Joints { get; } = new();", vm.ViewModelText);
		Assert.Contains("private void OnThemeSwitchChanged(object? sender, RoutedEventArgs e)", vm.CodeBehindText);
		window.SaveScreen("main-robot-file");
		window.Close();
	}

	[AvaloniaFact]
	public void An_edit_in_a_big_document_is_fast()
	{
		var (window, vm, _) = Open(RobotDocument());
		var text = vm.Document.Root.Descendants().First(n => n.TypeName == "TextBlock" && n.Has("Text"));
		vm.Document.Select(text);
		TestHelpers.Settle();

		vm.Document.SetAttribute(text, "Text", "warm up");
		TestHelpers.Settle();
		var watch = Stopwatch.StartNew();
		const int edits = 30;
		for (int i = 0; i < edits; i++)
		{
			vm.Document.SetAttribute(text, "Text", "Caption " + i);
			TestHelpers.Settle();
		}
		double perEdit = watch.Elapsed.TotalMilliseconds / edits;

		Assert.True(perEdit < 250, $"one edit took {perEdit:F0} ms");
		TestHelpers.WriteMetric("one edit in the robot document (ms)", perEdit);
		window.Close();
	}

	[AvaloniaFact]
	public void Many_edits_do_not_fill_the_memory()
	{
		var (window, vm, _) = Open(RobotDocument());
		var text = vm.Document.Root.Descendants().First(n => n.TypeName == "TextBlock" && n.Has("Text"));

		void Edit(int from, int count)
		{
			for (int i = from; i < from + count; i++)
			{
				vm.Document.SetAttribute(text, "Text", "Caption " + i);
				if (i % 10 == 0)
					TestHelpers.Settle();
			}
			TestHelpers.Settle();
		}

		Edit(0, 400);
		long before = GC.GetTotalMemory(forceFullCollection: true);
		Edit(400, 400);
		long after = GC.GetTotalMemory(forceFullCollection: true);

		double growthMb = (after - before) / 1024.0 / 1024.0;
		Assert.True(growthMb < 25, $"400 edits kept {growthMb:F1} MB");
		TestHelpers.WriteMetric("memory kept by 400 edits (MB)", growthMb);
		window.Close();
	}

	[AvaloniaFact]
	public void Dragging_a_row_in_the_outline_moves_the_element()
	{
		var (window, vm, _) = Open(DocumentTemplates.Create(DocumentTemplates.All[3]));
		var outline = window.GetVisualDescendants().OfType<OutlineView>().Single();
		var list = outline.GetVisualDescendants().OfType<ListBox>().Single();
		var ok = vm.Document.Root.Descendants().First(n => n.Get("Content") == "OK");
		var cancel = vm.Document.Root.Descendants().First(n => n.Get("Content") == "Cancel");

		Point RowPoint(DesignNode node, double fractionY)
		{
			var container = list.ContainerFromItem(vm.Outline.Rows.First(r => r.Node == node))!;
			return container.TranslatePoint(new Point(120, container.Bounds.Height * fractionY), window)!.Value;
		}

		var from = RowPoint(ok, 0.5);
		var to = RowPoint(cancel, 0.9);
		window.MouseMove(from);
		window.MouseDown(from, MouseButton.Left);
		for (int i = 1; i <= 6; i++)
			window.MouseMove(from + (to - from) * (i / 6.0));
		window.SaveScreen("main-outline-drag");
		window.MouseUp(to, MouseButton.Left);
		TestHelpers.Settle();

		Assert.Equal(new[] { "Cancel", "OK" }, ok.Parent!.Children.Select(c => c.Get("Content")));
		window.Close();
	}

	[AvaloniaFact]
	public void The_dialogs_open_and_give_the_choice_back()
	{
		var owner = new Window { Width = 900, Height = 700 };
		owner.Show();

		var newDialog = new NewDocumentDialog("Robot");
		newDialog.Show(owner);
		TestHelpers.Settle();
		newDialog.SaveScreen("dialog-new");
		var list = newDialog.GetVisualDescendants().OfType<ListBox>().Single();
		list.SelectedIndex = 5;
		TestHelpers.Settle();
		Assert.Equal("MainView", newDialog.FindControl<TextBox>("ViewNameBox")!.Text);
		Assert.Contains("Robot.ViewModels.MainViewModel", newDialog.FindControl<TextBlock>("SummaryText")!.Text);
		newDialog.FindControl<TextBox>("ViewNameBox")!.Text = "9 bad";
		TestHelpers.Settle();
		Assert.False(newDialog.FindControl<Button>("CreateButton")!.IsEnabled);
		newDialog.Close();

		var document = DocumentTemplates.Create(DocumentTemplates.All[1], "Robot");
		var exportDialog = new ExportDialog(Path.GetTempPath(),
			project => ProjectExporter.Generate(document, new ExportOptions { CreateProject = project }));
		exportDialog.Show(owner);
		TestHelpers.Settle();
		var files = exportDialog.FindControl<ItemsControl>("FileList")!;
		Assert.Equal(3, files.ItemCount);
		exportDialog.FindControl<RadioButton>("NewProjectOption")!.IsChecked = true;
		TestHelpers.Settle();
		Assert.Equal(8, files.ItemCount);
		exportDialog.SaveScreen("dialog-export");
		exportDialog.Close();
		owner.Close();
	}

	[AvaloniaFact]
	public void The_preview_window_shows_the_design_in_a_real_window()
	{
		var owner = new Window { Width = 900, Height = 700 };
		owner.Show();
		var service = new DialogService(owner);
		var document = DocumentTemplates.Create(DocumentTemplates.All[1]);
		int before = owner.OwnedWindows.Count;

		service.ShowPreviewWindow(document, Avalonia.Styling.ThemeVariant.Dark);
		TestHelpers.Settle();

		var preview = Assert.Single(owner.OwnedWindows.Skip(before));
		Assert.Equal("Control panel (preview)", preview.Title);
		Assert.Equal(1000, preview.Width);
		Assert.Equal(640, preview.Height);
		Assert.Contains(preview.GetVisualDescendants().OfType<Slider>(), _ => true);
		preview.SaveScreen("preview-window-dark");
		preview.Close();
		owner.Close();
	}

	[AvaloniaFact]
	public void The_code_tabs_and_the_menus_can_be_seen()
	{
		var (window, vm, _) = Open(DocumentTemplates.Create(DocumentTemplates.All[1]));
		var tabs = window.GetVisualDescendants().OfType<CodeView>().Single().GetVisualDescendants().OfType<TabControl>().First();

		tabs.SelectedIndex = 2;
		TestHelpers.Settle();
		window.SaveScreen("main-viewmodel-tab");
		tabs.SelectedIndex = 1;
		TestHelpers.Settle();
		window.SaveScreen("main-codebehind-tab");

		var button = vm.Document.Root.Descendants().First(n => n.TypeName == "Button");
		var surface = window.GetVisualDescendants().OfType<DesignSurface>().Single();
		var overlay = surface.GetVisualDescendants().OfType<DesignOverlay>().Single();
		var rect = surface.BoundsOf(button)!.Value;
		var point = overlay.TranslatePoint(rect.Center, window)!.Value;
		window.MouseMove(point);
		window.MouseDown(point, MouseButton.Right);
		window.MouseUp(point, MouseButton.Right);
		TestHelpers.Settle();
		Assert.Same(button, vm.SelectedNode);
		window.SaveScreen("main-context-menu");
		window.Close();
	}

	[AvaloniaFact]
	public async Task Typing_in_the_xaml_editor_changes_the_design_after_a_short_pause()
	{
		var (window, vm, _) = Open();
		var editor = window.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();
		Assert.Equal(vm.XamlText, editor.Text);

		int offset = editor.Text.IndexOf("Title=\"MainWindow", StringComparison.Ordinal) + "Title=\"Main".Length;
		editor.TextArea.Focus();
		editor.CaretOffset = offset;
		window.KeyTextInput("Big");
		TestHelpers.Settle();
		Assert.Equal("MainWindow", vm.Document.Root.Get("Title"));

		for (int i = 0; i < 20 && vm.Document.Root.Get("Title") == "MainWindow"; i++)
		{
			await Task.Delay(100);
			TestHelpers.Settle();
		}
		Assert.Equal("MainBigWindow", vm.Document.Root.Get("Title"));
		Assert.Contains("Title=\"MainBigWindow\"", editor.Text);
		Assert.True(vm.Document.CanUndo);

		var grid = vm.Document.Root.ContentChildren.Single();
		editor.TextArea.Caret.Line = grid.SourceLine;
		TestHelpers.Settle();
		Assert.Same(grid, vm.SelectedNode);

		vm.UndoCommand.Execute(null);
		TestHelpers.Settle();
		Assert.Contains("Title=\"MainWindow\"", editor.Text);
		window.Close();
	}

	[AvaloniaFact]
	public void Files_without_a_visual_root_open_without_a_preview()
	{
		const string header = "xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
		foreach (string xaml in new[]
		{
			$"<Application {header} x:Class=\"Demo.App\"><Application.Styles><FluentTheme /></Application.Styles></Application>",
			$"<Styles {header}><Style Selector=\"Button\"><Setter Property=\"Margin\" Value=\"4\" /></Style></Styles>",
			$"<ResourceDictionary {header}><SolidColorBrush x:Key=\"Accent\" Color=\"Red\" /></ResourceDictionary>",
			$"<local:Thing {header} xmlns:local=\"using:Demo\" />",
		})
		{
			var (window, vm, _) = Open(DesignXamlReader.Parse(xaml));
			var surface = window.GetVisualDescendants().OfType<DesignSurface>().Single();

			Assert.Null(surface.Preview!.Root);
			Assert.True(surface.FindControl<TextBlock>("EmptyMessage")!.IsVisible);
			Assert.NotEmpty(vm.Outline.Rows);
			vm.Document.Select(vm.Document.Root.Children.FirstOrDefault() ?? vm.Document.Root);
			TestHelpers.Settle();
			Assert.True(vm.Properties.HasTarget);
			window.Close();
		}
	}

	[AvaloniaFact]
	public void Run_mode_hides_the_selection_and_design_mode_brings_it_back()
	{
		var (window, vm, _) = Open(DocumentTemplates.Create(DocumentTemplates.All[1]));
		var surface = window.GetVisualDescendants().OfType<DesignSurface>().Single();
		var overlay = surface.GetVisualDescendants().OfType<DesignOverlay>().Single();

		vm.ToggleRunModeCommand.Execute(null);
		TestHelpers.Settle();
		Assert.False(overlay.IsVisible);
		window.SaveScreen("main-run-mode");

		vm.ToggleRunModeCommand.Execute(null);
		TestHelpers.Settle();
		Assert.True(overlay.IsVisible);
		window.Close();
	}
}
