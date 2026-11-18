using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace AvaloniaBuilder.Tests;

internal static class TestHelpers
{
	public static string ScreensFolder
	{
		get
		{
			string? folder = AppContext.BaseDirectory;
			while (folder is not null && !File.Exists(Path.Combine(folder, "AvaloniaBuilder.slnx")))
				folder = Path.GetDirectoryName(folder);
			string screens = Path.Combine(folder ?? AppContext.BaseDirectory, "artifacts", "screens");
			Directory.CreateDirectory(screens);
			return screens;
		}
	}

	public static string SaveScreen(this TopLevel window, string name)
	{
		Dispatcher.UIThread.RunJobs();
		string path = Path.Combine(ScreensFolder, name + ".png");
		using var frame = window.CaptureRenderedFrame();
		if (frame is not null)
		{
			using var stream = File.Create(path);
#pragma warning disable CS0618
			frame.Save(stream);
#pragma warning restore CS0618
		}
		return path;
	}

	public static Point CenterOf(this Control control, Visual relativeTo) =>
		control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

	public static void WriteMetric(string name, double value)
	{
		string file = Path.Combine(Path.GetDirectoryName(ScreensFolder)!, "metrics.txt");
		File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {name}: {value:F1}{Environment.NewLine}");
	}

	public static void Settle() => Dispatcher.UIThread.RunJobs();
}
