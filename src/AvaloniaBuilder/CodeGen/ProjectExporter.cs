using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.CodeGen;

public sealed class ExportOptions
{
	public bool CreateProject { get; init; }

	public string ProjectName { get; init; } = "";

	public bool IncludeViewModel { get; init; } = true;

	public CodeGenOptions CodeGen { get; init; } = new();
}

public sealed record GeneratedFile(string RelativePath, string Content);

public sealed record ExportResult(IReadOnlyList<string> Written, IReadOnlyList<string> Skipped);

public static class ProjectExporter
{
	public const string AvaloniaVersion = "12.1.6";
	public const string ToolkitVersion = "8.4.2";

	public static string GetRootNamespace(DesignDocument document)
	{
		string ns = CodeNames.Split(document.ViewClass ?? ViewModelGenerator.DefaultViewClass).Namespace;
		if (ns.EndsWith(".Views", StringComparison.Ordinal))
			return ns[..^".Views".Length];
		return ns.Length == 0 ? "MyApp" : ns;
	}

	public static IReadOnlyList<GeneratedFile> Generate(DesignDocument document, ExportOptions options)
	{
		string viewClass = document.ViewClass ?? ViewModelGenerator.DefaultViewClass;
		string viewModelClass = ViewModelGenerator.GetViewModelClass(document);
		string root = options.ProjectName.Length > 0 ? options.ProjectName : GetRootNamespace(document);
		var (viewNamespace, viewName) = CodeNames.Split(viewClass);
		var (viewModelNamespace, viewModelName) = CodeNames.Split(viewModelClass);

		var files = new List<GeneratedFile>();
		string viewFolder = FolderFor(viewNamespace, root);
		files.Add(new GeneratedFile(Combine(viewFolder, viewName + ".axaml"), DesignXamlWriter.Write(WithClassNames(document, viewClass, options.IncludeViewModel ? viewModelClass : null))));
		files.Add(new GeneratedFile(Combine(viewFolder, viewName + ".axaml.cs"), CodeBehindGenerator.Generate(WithClassNames(document, viewClass, null))));
		if (options.IncludeViewModel)
		{
			files.Add(new GeneratedFile(Combine(FolderFor(viewModelNamespace, root), viewModelName + ".cs"),
				ViewModelGenerator.Generate(WithClassNames(document, viewClass, viewModelClass), options.CodeGen)));
			if (options.CodeGen.ViewModelBaseClass == "ViewModelBase")
				files.Add(new GeneratedFile(Combine(FolderFor(viewModelNamespace, root), "ViewModelBase.cs"), ViewModelBase(viewModelNamespace)));
		}

		if (options.CreateProject)
		{
			bool rootIsWindow = DesignRules.IsWindow(XamlTypes.Resolve(document, document.Root));
			string windowClass = viewClass;
			if (!rootIsWindow)
			{
				string hostName = viewName == "MainWindow" ? "HostWindow" : "MainWindow";
				windowClass = (viewNamespace.Length > 0 ? viewNamespace + "." : "") + hostName;
				files.Add(new GeneratedFile(Combine(viewFolder, hostName + ".axaml"), HostWindowXaml(windowClass, viewNamespace, viewName, root)));
				files.Add(new GeneratedFile(Combine(viewFolder, hostName + ".axaml.cs"), HostWindowCode(viewNamespace, hostName)));
			}

			files.Add(new GeneratedFile(root + ".csproj", ProjectFile()));
			files.Add(new GeneratedFile("Program.cs", ProgramCode(root)));
			files.Add(new GeneratedFile("App.axaml", AppXaml(root)));
			files.Add(new GeneratedFile("App.axaml.cs", AppCode(root, windowClass, options.IncludeViewModel ? viewModelClass : null)));
			files.Add(new GeneratedFile("app.manifest", Manifest(root)));
		}
		return files;
	}

	public static ExportResult Write(IReadOnlyList<GeneratedFile> files, string targetDirectory, bool overwrite)
	{
		var written = new List<string>();
		var skipped = new List<string>();
		foreach (var file in files)
		{
			string path = Path.Combine(targetDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(path) && !overwrite)
			{
				skipped.Add(file.RelativePath);
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, file.Content.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
			written.Add(file.RelativePath);
		}
		return new ExportResult(written, skipped);
	}

	private static DesignDocument WithClassNames(DesignDocument document, string viewClass, string? viewModelClass)
	{
		var copy = new DesignDocument(document.Root.Clone(), document.Namespaces);
		if (copy.ViewClass != viewClass)
			copy.Root.Set("x:Class", viewClass);
		if (viewModelClass is not null && copy.ViewModelClass != viewModelClass)
			copy.SetViewModelClass(viewModelClass);
		return copy;
	}

	private static string FolderFor(string ns, string root)
	{
		if (ns == root || ns.Length == 0)
			return "";
		if (ns.StartsWith(root + ".", StringComparison.Ordinal))
			return ns[(root.Length + 1)..].Replace('.', '/');
		return ns[(ns.LastIndexOf('.') + 1)..];
	}

	private static string Combine(string folder, string file) => folder.Length == 0 ? file : folder + "/" + file;

	private static string ProjectFile() => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ApplicationManifest>app.manifest</ApplicationManifest>
            <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Avalonia" Version="{AvaloniaVersion}" />
            <PackageReference Include="Avalonia.Desktop" Version="{AvaloniaVersion}" />
            <PackageReference Include="Avalonia.Themes.Fluent" Version="{AvaloniaVersion}" />
            <PackageReference Include="Avalonia.Fonts.Inter" Version="{AvaloniaVersion}" />
            <PackageReference Include="CommunityToolkit.Mvvm" Version="{ToolkitVersion}" />
          </ItemGroup>
        </Project>

        """;

	private static string ProgramCode(string root) => $$"""
        using System;
        using Avalonia;

        namespace {{root}};

        sealed class Program
        {
            [STAThread]
            public static void Main(string[] args) => BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            public static AppBuilder BuildAvaloniaApp()
                => AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .WithInterFont()
                    .LogToTrace();
        }

        """;

	private static string AppXaml(string root) => $"""
        <Application xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     x:Class="{root}.App"
                     RequestedThemeVariant="Default">
            <Application.Styles>
                <FluentTheme />
            </Application.Styles>
        </Application>

        """;

	private static string AppCode(string root, string windowClass, string? viewModelClass)
	{
		var (windowNamespace, windowName) = CodeNames.Split(windowClass);
		var usings = new SortedSet<string>(StringComparer.Ordinal)
		{
			"Avalonia", "Avalonia.Controls.ApplicationLifetimes", "Avalonia.Markup.Xaml",
		};
		if (windowNamespace.Length > 0 && windowNamespace != root)
			usings.Add(windowNamespace);
		string create = $"new {windowName}()";
		if (viewModelClass is not null)
		{
			var (viewModelNamespace, viewModelName) = CodeNames.Split(viewModelClass);
			if (viewModelNamespace.Length > 0 && viewModelNamespace != root)
				usings.Add(viewModelNamespace);
			create = $"new {windowName} {{ DataContext = new {viewModelName}() }}";
		}

		return string.Join("\n", CodeNames.SortUsings(usings).Select(u => $"using {u};")) + $$"""


            namespace {{root}};

            public partial class App : Application
            {
                public override void Initialize()
                {
                    AvaloniaXamlLoader.Load(this);
                }

                public override void OnFrameworkInitializationCompleted()
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.MainWindow = {{create}};
                    }

                    base.OnFrameworkInitializationCompleted();
                }
            }

            """;
	}

	private static string ViewModelBase(string ns) => $$"""
        using CommunityToolkit.Mvvm.ComponentModel;

        namespace {{ns}};

        public abstract class ViewModelBase : ObservableObject
        {
        }

        """;

	private static string HostWindowXaml(string windowClass, string viewNamespace, string viewName, string root) => $"""
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:views="using:{viewNamespace}"
                x:Class="{windowClass}"
                Title="{root}"
                Width="900" Height="600">
            <views:{viewName} />
        </Window>

        """;

	private static string HostWindowCode(string ns, string name) => $$"""
        using Avalonia.Controls;

        namespace {{ns}};

        public partial class {{name}} : Window
        {
            public {{name}}()
            {
                InitializeComponent();
            }
        }

        """;

	private static string Manifest(string root) => $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
          <assemblyIdentity version="1.0.0.0" name="{{root}}.Desktop"/>

          <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
            <application>
              <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
            </application>
          </compatibility>
        </assembly>

        """;
}
