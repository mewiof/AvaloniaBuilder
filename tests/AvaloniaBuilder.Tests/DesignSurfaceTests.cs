using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaBuilder.Designer;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Tests;

public class DesignSurfaceTests
{
	private const string Header = "xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

	private static (Window Window, DesignSurface Surface, DesignDocument Document) Open(string xaml, double width = 1100, double height = 760)
	{
		var document = DesignXamlReader.Parse(xaml);
		var surface = new DesignSurface { Document = document };
		var window = new Window { Width = width, Height = height, Content = surface };
		window.Show();
		TestHelpers.Settle();
		return (window, surface, document);
	}

	private static DesignOverlay OverlayOf(DesignSurface surface) => surface.GetVisualDescendants().OfType<DesignOverlay>().Single();

	private static Point Middle(Window window, DesignSurface surface, DesignNode node, double fractionX = 0.5, double fractionY = 0.5)
	{
		var rect = surface.BoundsOf(node)!.Value;
		var inOverlay = new Point(rect.X + rect.Width * fractionX, rect.Y + rect.Height * fractionY);
		return OverlayOf(surface).TranslatePoint(inOverlay, window)!.Value;
	}

	private static void Click(Window window, Point point)
	{
		window.MouseMove(point);
		window.MouseDown(point, MouseButton.Left);
		window.MouseUp(point, MouseButton.Left);
		TestHelpers.Settle();
	}

	private static void Drag(Window window, Point from, Point to)
	{
		window.MouseMove(from);
		window.MouseDown(from, MouseButton.Left);
		for (int i = 1; i <= 8; i++)
			window.MouseMove(from + (to - from) * (i / 8.0));
		window.MouseUp(to, MouseButton.Left);
		TestHelpers.Settle();
	}

	[AvaloniaFact]
	public void The_surface_shows_the_design_and_a_click_selects_the_element_under_the_mouse()
	{
		var (window, surface, document) = Open(DesignXamlWriter.Write(DocumentTemplates.Create(DocumentTemplates.All[1])));
		var slider = document.Root.Descendants().First(n => n.TypeName == "Slider");

		Click(window, Middle(window, surface, slider));

		Assert.Same(slider, document.Selected);
		window.SaveScreen("surface-control-panel");
		window.Close();
	}

	[AvaloniaFact]
	public void A_click_does_not_press_the_real_button()
	{
		var (window, surface, document) = Open($"<Window {Header} Width=\"300\" Height=\"200\"><ToggleButton Content=\"Toggle\" /></Window>");
		var node = document.Root.Children[0];

		Click(window, Middle(window, surface, node));

		Assert.Same(node, document.Selected);
		Assert.False(((Avalonia.Controls.Primitives.ToggleButton)surface.Preview!.GetControl(node)!).IsChecked);
		window.Close();
	}

	[AvaloniaFact]
	public void Dragging_an_element_in_a_stack_panel_changes_the_order()
	{
		var (window, surface, document) = Open($"""
            <Window {Header} Width="400" Height="300">
                <StackPanel Spacing="8">
                    <Button Content="One" />
                    <Button Content="Two" />
                    <Button Content="Three" />
                </StackPanel>
            </Window>
            """);
		var panel = document.Root.Children[0];
		var one = panel.Children[0];
		var three = panel.Children[2];

		Drag(window, Middle(window, surface, one), Middle(window, surface, three, 0.5, 0.9));

		Assert.Equal(new[] { "Two", "Three", "One" }, panel.Children.Select(c => c.Get("Content")));
		Assert.True(document.CanUndo);
		document.Undo();
		Assert.Equal(new[] { "One", "Two", "Three" }, document.Root.Children[0].Children.Select(c => c.Get("Content")));
		window.Close();
	}

	[AvaloniaFact]
	public void Dragging_an_element_into_another_grid_cell_sets_row_and_column()
	{
		var (window, surface, document) = Open($"""
            <Window {Header} Width="400" Height="300">
                <Grid RowDefinitions="*,*" ColumnDefinitions="*,*">
                    <Button Content="Move me" />
                </Grid>
            </Window>
            """);
		var grid = document.Root.Children[0];
		var button = grid.Children[0];

		Drag(window, Middle(window, surface, button), Middle(window, surface, grid, 0.75, 0.75));

		Assert.Equal("1", button.Get("Grid.Row"));
		Assert.Equal("1", button.Get("Grid.Column"));
		window.Close();
	}

	[AvaloniaFact]
	public void Dragging_a_handle_changes_width_and_height()
	{
		var (window, surface, document) = Open($"""
            <Window {Header} Width="400" Height="300">
                <Canvas>
                    <Rectangle Canvas.Left="50" Canvas.Top="40" Width="100" Height="60" Fill="SteelBlue" />
                </Canvas>
            </Window>
            """);
		var rectangle = document.Root.Children[0].Children[0];
		Click(window, Middle(window, surface, rectangle));

		var corner = Middle(window, surface, rectangle, 1, 1);
		Drag(window, corner, corner + new Vector(30, 20));

		Assert.Equal("130", rectangle.Get("Width"));
		Assert.Equal("80", rectangle.Get("Height"));

		var middle = Middle(window, surface, rectangle);
		Drag(window, middle, middle + new Vector(40, 25));
		Assert.Equal("90", rectangle.Get("Canvas.Left"));
		Assert.Equal("65", rectangle.Get("Canvas.Top"));
		window.Close();
	}

	[AvaloniaFact]
	public void The_window_itself_can_be_resized_with_its_handle()
	{
		var (window, surface, document) = Open($"<Window {Header} Width=\"400\" Height=\"300\"><Grid /></Window>");
		document.Select(document.Root);
		TestHelpers.Settle();

		var corner = Middle(window, surface, document.Root, 1, 1);
		Drag(window, corner, corner + new Vector(100, 50));

		Assert.Equal("500", document.Root.Get("Width"));
		Assert.Equal("350", document.Root.Get("Height"));
		window.Close();
	}

	[AvaloniaFact]
	public void A_user_control_gets_a_design_size_instead_of_a_real_size()
	{
		var (window, surface, document) = Open($"<UserControl {Header}><Grid /></UserControl>");
		document.Select(document.Root);
		TestHelpers.Settle();

		var corner = Middle(window, surface, document.Root, 1, 1);
		Drag(window, corner, corner + new Vector(-200, -100));

		Assert.Null(document.Root.Get("Width"));
		Assert.Equal("600", document.Root.Get("d:DesignWidth"));
		Assert.Equal("350", document.Root.Get("d:DesignHeight"));
		Assert.Equal("d", document.Root.Get("mc:Ignorable"));
		Assert.NotNull(document.GetNamespaceUri("d"));
		window.Close();
	}

	[AvaloniaFact]
	public void A_toolbox_entry_can_be_dropped_into_a_dock_panel_edge()
	{
		var (window, surface, document) = Open($"""
            <Window {Header} Width="500" Height="400">
                <DockPanel>
                    <Grid />
                </DockPanel>
            </Window>
            """);
		var dock = document.Root.Children[0];
		var item = ToolboxCatalog.Items.First(i => i.Name == "Button");

		surface.BeginToolDrag(item);
		var overlay = OverlayOf(surface);
		var top = Middle(window, surface, dock, 0.5, 0) + new Vector(0, 4);
		PointerEventArgs? last = null;
		void Capture(object? s, PointerEventArgs e) => last = e;
		overlay.PointerMoved += Capture;
		window.MouseMove(top);
		overlay.PointerMoved -= Capture;
		Assert.NotNull(last);
		Assert.True(surface.UpdateToolDrag(last));
		window.SaveScreen("surface-tool-drag");
		Assert.True(surface.CompleteToolDrag());
		TestHelpers.Settle();

		Assert.Equal(new[] { "Button", "Grid" }, dock.Children.Select(c => c.TypeName));
		Assert.Equal("Top", dock.Children[0].Get("DockPanel.Dock"));
		Assert.Same(dock.Children[0], document.Selected);
		window.Close();
	}

	[AvaloniaFact]
	public void Selecting_something_in_a_closed_tab_opens_the_tab()
	{
		var (window, surface, document) = Open($"""
            <Window {Header} Width="400" Height="300">
                <TabControl>
                    <TabItem Header="One"><TextBlock Text="first" /></TabItem>
                    <TabItem Header="Two"><TextBlock Text="second" /></TabItem>
                </TabControl>
            </Window>
            """);
		var second = document.Root.Children[0].Children[1].Children[0];
		Assert.Null(surface.BoundsOf(second));

		document.Select(second);
		TestHelpers.Settle();

		Assert.NotNull(surface.BoundsOf(second));
		window.SaveScreen("surface-tabs");
		window.Close();
	}

	[AvaloniaFact]
	public void In_run_mode_the_controls_work()
	{
		var (window, surface, document) = Open($"<Window {Header} Width=\"300\" Height=\"200\"><ToggleButton Content=\"Toggle\" /></Window>");
		var node = document.Root.Children[0];
		surface.IsRunMode = true;
		TestHelpers.Settle();

		var control = (Avalonia.Controls.Primitives.ToggleButton)surface.Preview!.GetControl(node)!;
		Click(window, control.CenterOf(window));

		Assert.True(control.IsChecked);
		Assert.Null(document.Selected);
		window.Close();
	}

	[AvaloniaFact]
	public void The_arrow_keys_move_an_element_in_a_canvas_and_escape_selects_the_parent()
	{
		var (window, surface, document) = Open($"""
            <Window {Header} Width="400" Height="300">
                <Canvas>
                    <Button Canvas.Left="10" Canvas.Top="10" Content="Go" />
                </Canvas>
            </Window>
            """);
		var button = document.Root.Children[0].Children[0];
		Click(window, Middle(window, surface, button));

		window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
		window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.ArrowDown, null);
		TestHelpers.Settle();
		Assert.Equal("11", button.Get("Canvas.Left"));
		Assert.Equal("20", button.Get("Canvas.Top"));

		window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
		Assert.Same(button.Parent, document.Selected);
		window.Close();
	}
}
