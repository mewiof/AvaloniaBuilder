using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Preview;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Tests;

public class CodeGenTests
{
	private const string Header = "xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

	private static DesignDocument Window(string content, string extra = "") => DesignXamlReader.Parse(
		$"<Window {Header} xmlns:vm=\"using:Demo.ViewModels\" x:Class=\"Demo.Views.MainWindow\" x:DataType=\"vm:MainViewModel\" {extra}>{content}</Window>");

	[AvaloniaFact]
	public void The_view_model_gets_a_property_for_every_binding_with_the_right_type()
	{
		var document = Window("""
            <StackPanel>
                <TextBox Text="{Binding Name}" />
                <Slider Value="{Binding Speed}" />
                <TextBlock Text="{Binding Speed, StringFormat={}{0:F1}}" />
                <TextBlock Text="{Binding Temperature, StringFormat={}{0:F1} °C}" />
                <CheckBox IsChecked="{Binding IsActive}" Content="{Binding Caption}" />
                <Button IsEnabled="{Binding !IsBusy}" Command="{Binding SaveCommand}" />
                <Button Command="{Binding DeleteCommand}" CommandParameter="42" />
                <NumericUpDown Value="{Binding Count}" />
                <ComboBox ItemsSource="{Binding Options}" SelectedItem="{Binding SelectedOption}" SelectedIndex="{Binding Index}" />
                <Border Background="{Binding Fill}" Opacity="{Binding Dim, Converter={StaticResource Conv}}" />
                <DatePicker SelectedDate="{Binding Day}" />
                <TextBlock Text="{Binding #Other.Text}" />
                <TextBlock Text="{Binding $parent[Window].Title}" />
            </StackPanel>
            """);

		string code = ViewModelGenerator.Generate(document);

		Assert.Contains("namespace Demo.ViewModels;", code);
		Assert.Contains("public partial class MainViewModel : ObservableObject", code);
		Assert.Contains("public partial string Name { get; set; } = \"\";", code);
		Assert.Contains("public partial double Speed { get; set; }", code);
		Assert.Contains("public partial double Temperature { get; set; }", code);
		Assert.Contains("public partial bool IsActive { get; set; }", code);
		Assert.Contains("public partial string Caption { get; set; } = \"\";", code);
		Assert.Contains("public partial bool IsBusy { get; set; }", code);
		Assert.Contains("public partial decimal? Count { get; set; }", code);
		Assert.Contains("public ObservableCollection<string> Options { get; } = new();", code);
		Assert.Contains("public partial string? SelectedOption { get; set; }", code);
		Assert.Contains("public partial int Index { get; set; }", code);
		Assert.Contains("public partial IBrush? Fill { get; set; }", code);
		Assert.Contains("public partial object? Dim { get; set; }", code);
		Assert.Contains("public partial DateTimeOffset? Day { get; set; }", code);
		Assert.Contains("[RelayCommand]\n    private void Save()", code);
		Assert.Contains("private void Delete(object? parameter)", code);
		Assert.Contains("using System.Collections.ObjectModel;", code);
		Assert.Contains("using Avalonia.Media;", code);
		Assert.DoesNotContain("Other", code);
		Assert.DoesNotContain("Title", code);
	}

	[AvaloniaFact]
	public void Nested_paths_and_item_templates_get_their_own_classes()
	{
		var document = Window("""
            <StackPanel>
                <Slider Value="{Binding Shoulder.Target}" Maximum="{Binding Shoulder.Maximum}" />
                <StackPanel DataContext="{Binding Camera}">
                    <TextBlock Text="{Binding Fps}" />
                </StackPanel>
                <ItemsControl ItemsSource="{Binding Joints}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel>
                                <TextBlock Text="{Binding Name}" />
                                <Slider Value="{Binding Angle}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ListBox ItemsSource="{Binding Log}">
                    <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:LogEntry">
                            <TextBlock Text="{Binding Message}" />
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
            </StackPanel>
            """);

		var classes = ViewModelGenerator.Analyze(document);
		string code = ViewModelGenerator.Generate(document);

		Assert.Equal(new[] { "MainViewModel", "ShoulderViewModel", "CameraViewModel", "JointViewModel", "LogEntry" }, classes.Select(c => c.Name));
		Assert.Contains("public ShoulderViewModel Shoulder { get; } = new();", code);
		Assert.Contains("public CameraViewModel Camera { get; } = new();", code);
		Assert.Contains("public ObservableCollection<JointViewModel> Joints { get; } = new();", code);
		Assert.Contains("public ObservableCollection<LogEntry> Log { get; } = new();", code);
		Assert.Contains("public partial class JointViewModel : ObservableObject", code);
		string main = code[..code.IndexOf("public partial class ShoulderViewModel", StringComparison.Ordinal)];
		Assert.DoesNotContain(" Name ", main);
		Assert.DoesNotContain(" Fps ", main);
	}

	[AvaloniaFact]
	public void The_old_field_style_is_an_option()
	{
		var document = Window("<TextBox Text=\"{Binding Name}\" />");

		string code = ViewModelGenerator.Generate(document, new CodeGenOptions { UsePartialProperties = false, ViewModelBaseClass = "ViewModelBase" });

		Assert.Contains("public partial class MainViewModel : ViewModelBase", code);
		Assert.Contains("[ObservableProperty]\n    private string _name = \"\";", code);
	}

	[AvaloniaFact]
	public void The_code_behind_has_a_method_for_every_event_handler()
	{
		var document = Window("""
            <StackPanel>
                <Button x:Name="SaveButton" Click="OnSaveClick" />
                <TextBox TextChanged="OnTextChanged" KeyDown="OnKeyDown" />
                <ToggleSwitch IsCheckedChanged="OnSaveClick" />
            </StackPanel>
            """, "Closing=\"OnClosing\"");

		string code = CodeBehindGenerator.Generate(document);

		Assert.Contains("namespace Demo.Views;", code);
		Assert.Contains("public partial class MainWindow : Window", code);
		Assert.Contains("InitializeComponent();", code);
		Assert.Contains("private void OnSaveClick(object? sender, RoutedEventArgs e)", code);
		Assert.Contains("private void OnTextChanged(object? sender, TextChangedEventArgs e)", code);
		Assert.Contains("private void OnKeyDown(object? sender, KeyEventArgs e)", code);
		Assert.Contains("private void OnClosing(object? sender, WindowClosingEventArgs e)", code);
		Assert.Contains("using Avalonia.Interactivity;", code);
		Assert.Contains("using Avalonia.Input;", code);
		Assert.Equal(1, code.Split("OnSaveClick(").Length - 1);
	}

	[AvaloniaFact]
	public void Class_names_are_suggested_from_the_view_name()
	{
		Assert.Equal("MyApp.ViewModels.MainViewModel", ViewModelGenerator.SuggestViewModelClass("MyApp.Views.MainWindow"));
		Assert.Equal("MyApp.ViewModels.SettingsViewModel", ViewModelGenerator.SuggestViewModelClass("MyApp.Views.SettingsView"));
		Assert.Equal("MyApp.ViewModels.JointEditorViewModel", ViewModelGenerator.SuggestViewModelClass("MyApp.JointEditor"));
	}

	[AvaloniaFact]
	public void Every_toolbox_entry_is_valid_xaml_and_shows_without_problems()
	{
		foreach (var item in ToolboxCatalog.Items)
		{
			var document = DesignXamlReader.Parse($"<UserControl {Header} xmlns:local=\"using:MyApp\"><StackPanel /></UserControl>");
			var panel = document.Root.Children[0];
			foreach (var node in item.Create(document, out _))
			{
				DesignNode parent = panel;
				string? containerName = node.TypeName switch
				{
					"TabItem" => "TabControl",
					"MenuItem" => "Menu",
					"ListBoxItem" => "ListBox",
					"ComboBoxItem" => "ComboBox",
					"TreeViewItem" => "TreeView",
					_ => null,
				};
				if (containerName is not null)
				{
					parent = new DesignNode(containerName);
					panel.Add(parent);
				}
				Assert.True(DesignRules.CanAddChild(document, parent, node), $"{item.Name}: cannot be added to {parent.TypeName}");
				parent.Add(node);
			}

			using var result = PreviewBuilder.Build(document);
			Assert.True(result.Diagnostics.Count == 0, $"{item.Name}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");

			if (!item.Xaml.Contains("local:"))
			{
				string xaml = DesignXamlWriter.Write(document);
				var loaded = AvaloniaRuntimeXamlLoader.Load(xaml, typeof(App).Assembly);
				Assert.NotNull(loaded);
			}
		}
		Assert.True(ToolboxCatalog.Items.Count > 60);
		Assert.All(ToolboxCatalog.Items, item => Assert.Contains(item.Category, ToolboxCatalog.Categories));
	}

	[AvaloniaFact]
	public void Every_template_gives_a_document_that_shows_without_problems()
	{
		foreach (var template in DocumentTemplates.All)
		{
			var document = DocumentTemplates.Create(template, "Demo", "StartView");

			Assert.Equal("Demo.Views.StartView", document.ViewClass);
			Assert.Equal("Demo.ViewModels.StartViewModel", document.ViewModelClass);
			Assert.False(document.IsDirty);
			using var result = PreviewBuilder.Build(document);
			Assert.NotNull(result.Root);
			Assert.True(result.Diagnostics.Count == 0, $"{template.Name}: {string.Join("; ", result.Diagnostics.Select(d => d.Message))}");
		}
	}

	[AvaloniaFact]
	public void Export_lists_the_files_for_a_new_project()
	{
		var document = DocumentTemplates.Create(DocumentTemplates.All[0], "Demo", "MainWindow");

		var files = ProjectExporter.Generate(document, new ExportOptions { CreateProject = true });

		Assert.Equal(
			new[] { "Views/MainWindow.axaml", "Views/MainWindow.axaml.cs", "ViewModels/MainViewModel.cs", "Demo.csproj", "Program.cs", "App.axaml", "App.axaml.cs", "app.manifest" },
			files.Select(f => f.RelativePath));
		Assert.Contains("desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };", files.Single(f => f.RelativePath == "App.axaml.cs").Content);
	}

	[AvaloniaFact]
	[Trait("Category", "Slow")]
	public void Every_template_exports_a_project_that_builds()
	{
		string folder = Path.Combine(Path.GetTempPath(), "AvaloniaBuilderTests", Guid.NewGuid().ToString("N"));
		try
		{
			int index = 0;
			foreach (var template in DocumentTemplates.All)
			{
				var document = DocumentTemplates.Create(template, "Demo");
				if (index == 0)
				{
					var extra = DesignXamlReader.ParseFragment("""
                        <StackPanel>
                            <Button x:Name="ClickMe" Content="Click" Click="OnClickMe" Command="{Binding RunCommand}" CommandParameter="1" />
                            <Slider Value="{Binding Arm.Angle}" />
                            <ItemsControl ItemsSource="{Binding Joints}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate x:DataType="vm:JointViewModel">
                                        <TextBlock Text="{Binding Name}" />
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                            <ComboBox ItemsSource="{Binding Options}" SelectedItem="{Binding SelectedOption}" />
                        </StackPanel>
                        """, document, out _);
					document.InsertNode(document.Root.ContentChildren.First(), extra[0]);
				}

				var codeOptions = index == 1
					? new CodeGenOptions { UsePartialProperties = false, ViewModelBaseClass = "ViewModelBase" }
					: new CodeGenOptions();
				string target = Path.Combine(folder, "p" + index++);
				var files = ProjectExporter.Generate(document, new ExportOptions { CreateProject = true, CodeGen = codeOptions });
				var written = ProjectExporter.Write(files, target, overwrite: false);
				Assert.Empty(written.Skipped);

				var (exitCode, output) = Run("dotnet", "build -v q --nologo", target);
				Assert.True(exitCode == 0, $"Template '{template.Name}' did not build:\n{output}");
				Assert.DoesNotContain("warning AVLN", output);
			}
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
		}
	}

	private static (int ExitCode, string Output) Run(string file, string arguments, string workingDirectory)
	{
		var info = new ProcessStartInfo(file, arguments)
		{
			WorkingDirectory = workingDirectory,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
		};
		foreach (string name in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBUILDUSESERVER" })
			info.Environment.Remove(name);
		using var process = Process.Start(info)!;
		string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();
		return (process.ExitCode, output);
	}
}
