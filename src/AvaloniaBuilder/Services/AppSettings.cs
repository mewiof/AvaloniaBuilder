using System.Text.Json;

namespace AvaloniaBuilder.Services;

public sealed class AppSettings
{
	public List<string> RecentFiles { get; set; } = new();

	public string? LastFolder { get; set; }

	public string? LastExportFolder { get; set; }

	public string DefaultNamespace { get; set; } = "MyApp";

	public bool UsePartialProperties { get; set; } = true;

	public string ViewModelBaseClass { get; set; } = "ObservableObject";

	public string AppTheme { get; set; } = "Default";

	public string PreviewTheme { get; set; } = "Same";

	public void AddRecentFile(string path)
	{
		RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
		RecentFiles.Insert(0, path);
		if (RecentFiles.Count > 10)
			RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
	}
}

public static class SettingsStore
{
	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

	public static string DefaultPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AvaloniaBuilder", "settings.json");

	public static AppSettings Load(string? path = null)
	{
		try
		{
			path ??= DefaultPath;
			if (File.Exists(path))
				return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
		}
		catch (Exception)
		{
		}
		return new AppSettings();
	}

	public static void Save(AppSettings settings, string? path = null)
	{
		try
		{
			path ??= DefaultPath;
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
		}
		catch (Exception)
		{
		}
	}
}
