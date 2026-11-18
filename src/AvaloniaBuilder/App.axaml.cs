using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaBuilder.Services;
using AvaloniaBuilder.Views;

namespace AvaloniaBuilder;

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
			var window = new MainWindow { WindowState = Avalonia.Controls.WindowState.Maximized };
			desktop.MainWindow = window;

			Dispatcher.UIThread.UnhandledException += (_, e) =>
			{
				e.Handled = true;
				string file = WriteErrorLog(e.Exception);
				window.ViewModel.Message = $"Something went wrong: {e.Exception.Message}  Your document is still open. Details: {file}";
			};

			string? path = desktop.Args?.FirstOrDefault(arg => !arg.StartsWith('-') && File.Exists(arg));
			if (path is not null)
				window.OpenFile(Path.GetFullPath(path));
		}

		base.OnFrameworkInitializationCompleted();
	}

	private static string WriteErrorLog(Exception exception)
	{
		string file = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "errors.log");
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(file)!);
			File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
		}
		catch (Exception)
		{
		}
		return file;
	}
}
