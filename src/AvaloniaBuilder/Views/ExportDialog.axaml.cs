using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Services;

namespace AvaloniaBuilder.Views;

public partial class ExportDialog : Window
{
	private readonly Func<bool, IReadOnlyList<GeneratedFile>> _listFiles;

	public ExportDialog() : this(null, _ => Array.Empty<GeneratedFile>())
	{
	}

	public ExportDialog(string? startFolder, Func<bool, IReadOnlyList<GeneratedFile>> listFiles)
	{
		_listFiles = listFiles;
		InitializeComponent();
		FolderBox.Text = startFolder ?? "";
		Update();
	}

	private string Folder => FolderBox.Text?.Trim() ?? "";

	private void OnOptionChanged(object? sender, RoutedEventArgs e) => Update();

	private void OnFolderChanged(object? sender, TextChangedEventArgs e) => Update();

	private void Update()
	{
		if (FileList is null || ExportButton is null || HintText is null || NewProjectOption is null)
			return;

		string folder = Folder;
		var files = _listFiles(NewProjectOption.IsChecked == true);
		int existing = 0;
		var lines = new List<string>();
		foreach (var file in files)
		{
			bool exists = false;
			try
			{
				exists = folder.Length > 0 && File.Exists(Path.Combine(folder, file.RelativePath));
			}
			catch (ArgumentException)
			{
			}
			if (exists)
				existing++;
			lines.Add(exists ? file.RelativePath + "   (is there already)" : file.RelativePath);
		}
		FileList.ItemsSource = lines;

		ExportButton.IsEnabled = folder.Length > 0 && Path.IsPathRooted(folder);
		HintText.Text = folder.Length == 0 ? "Pick a folder first."
			: !Path.IsPathRooted(folder) ? "Give the full path of the folder, for example C:\\Projects\\MyApp."
			: existing == 0 ? ""
			: $"{existing} file{(existing == 1 ? " is" : "s are")} there already. Without the check mark above, {(existing == 1 ? "it stays" : "they stay")} as {(existing == 1 ? "it is" : "they are")}.";
	}

	private async void OnBrowse(object? sender, RoutedEventArgs e)
	{
		var options = new FolderPickerOpenOptions { Title = "Folder for the exported files", AllowMultiple = false };
		if (Folder.Length > 0 && Directory.Exists(Folder))
			options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Folder);
		var folders = await StorageProvider.OpenFolderPickerAsync(options);
		if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
			FolderBox.Text = path;
	}

	private void OnExport(object? sender, RoutedEventArgs e)
	{
		if (ExportButton.IsEnabled)
			Close(new ExportChoice(Folder, NewProjectOption.IsChecked == true, OverwriteBox.IsChecked == true));
	}

	private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
