using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaBuilder.Designer;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Services;
using AvaloniaBuilder.ViewModels;
using AvaloniaBuilder.Views;

namespace AvaloniaBuilder.Tests;

public class ReadmeTourTest
{
	[AvaloniaFact]
	[Trait("Category", "Slow")]
	public async Task The_five_minute_tour_of_the_readme_works()
	{
		var dialogs = new FakeDialogs();
		var vm = new MainViewModel(dialogs, new AppSettings());
		var window = new MainWindow(vm) { Width = 1500, Height = 920 };
		window.Show();
		TestHelpers.Settle();
		var surface = window.GetVisualDescendants().OfType<DesignSurface>().Single();
		var overlay = surface.GetVisualDescendants().OfType<DesignOverlay>().Single();
		var toolbox = window.GetVisualDescendants().OfType<ToolboxView>().Single();

		Point InDesign(DesignNode node, double fx, double fy)
		{
			var rect = surface.BoundsOf(node)!.Value;
			return overlay.TranslatePoint(new Point(rect.X + rect.Width * fx, rect.Y + rect.Height * fy), window)!.Value;
		}

		void DragFromToolbox(string name, Point to)
		{
			vm.Toolbox.Filter = name;
			TestHelpers.Settle();
			var entry = toolbox.GetVisualDescendants().OfType<Border>()
				.First(b => b.Classes.Contains("tool") && b.DataContext is ToolboxItem item && item.Name == name);
			var from = entry.CenterOf(window);
			window.MouseMove(from);
			window.MouseDown(from, MouseButton.Left);
			for (int i = 1; i <= 8; i++)
				window.MouseMove(from + (to - from) * (i / 8.0));
			window.MouseUp(to, MouseButton.Left);
			TestHelpers.Settle();
		}

		var grid = Assert.Single(vm.Document.Root.ContentChildren);
		Assert.Equal("Grid", grid.TypeName);

		DragFromToolbox("StackPanel", InDesign(grid, 0.5, 0.5));
		var panel = Assert.Single(grid.Children);
		Assert.Equal("StackPanel", panel.TypeName);

		DragFromToolbox("Button", InDesign(panel, 0.5, 0.3));
		DragFromToolbox("Label + slider + value", InDesign(panel, 0.5, 0.6));
		Assert.Equal(new[] { "Button", "Grid" }, panel.Children.Select(c => c.TypeName));
		var button = panel.Children[0];

		var point = InDesign(button, 0.5, 0.5);
		window.MouseMove(point);
		window.MouseDown(point, MouseButton.Left);
		window.MouseUp(point, MouseButton.Left);
		TestHelpers.Settle();
		Assert.Same(button, vm.SelectedNode);
		var items = vm.Properties.Groups.SelectMany(g => g.Items).ToList();
		items.First(i => i.Name == "Content").Text = "Start";

		items.First(i => i.Name == "Command").Text = "StartCommand";
		TestHelpers.Settle();

		Assert.Contains("<Button Content=\"Start\" Command=\"{Binding StartCommand}\" />", vm.XamlText);
		Assert.Contains("[RelayCommand]\n    private void Start()", vm.ViewModelText);
		Assert.Contains("public partial double Speed { get; set; }", vm.ViewModelText);
		Assert.False(vm.HasProblems);
		window.SaveScreen("readme-tour");

		string folder = Path.Combine(Path.GetTempPath(), "AvaloniaBuilderTests", Guid.NewGuid().ToString("N"));
		try
		{
			dialogs.ExportChoice = new ExportChoice(folder, CreateProject: true, Overwrite: false);
			dialogs.Answer = "Close";
			await vm.ExportCommand.ExecuteAsync(null);
			Assert.True(File.Exists(Path.Combine(folder, "MyApp.csproj")));

			var info = new ProcessStartInfo("dotnet", "build -v q --nologo")
			{
				WorkingDirectory = folder,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
			};
			foreach (string name in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBUILDUSESERVER" })
				info.Environment.Remove(name);
			using var process = Process.Start(info)!;
			string output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
			await process.WaitForExitAsync();
			Assert.True(process.ExitCode == 0, output);
		}
		finally
		{
			try
			{
				Directory.Delete(folder, recursive: true);
			}
			catch (IOException)
			{
			}
			dialogs.Answer = "Don't save";
			window.Close();
		}
	}
}
