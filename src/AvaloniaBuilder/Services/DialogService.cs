using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Preview;
using AvaloniaBuilder.Views;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Services;

public sealed class DialogService : IDialogService
{
	private static readonly FilePickerFileType XamlFiles = new("Avalonia XAML") { Patterns = new[] { "*.axaml", "*.xaml" } };

	private readonly Window _owner;

	public DialogService(Window owner) => _owner = owner;

	public async Task<string?> PickOpenFileAsync(string? startFolder)
	{
		var options = new FilePickerOpenOptions
		{
			Title = "Open a XAML file",
			AllowMultiple = false,
			FileTypeFilter = new[] { XamlFiles, FilePickerFileTypes.All },
			SuggestedStartLocation = await FolderAsync(startFolder),
		};
		var files = await _owner.StorageProvider.OpenFilePickerAsync(options);
		return files.Count > 0 ? files[0].TryGetLocalPath() : null;
	}

	public async Task<string?> PickSaveFileAsync(string suggestedName, string? startFolder)
	{
		var options = new FilePickerSaveOptions
		{
			Title = "Save the XAML file",
			SuggestedFileName = suggestedName,
			DefaultExtension = "axaml",
			FileTypeChoices = new[] { XamlFiles },
			ShowOverwritePrompt = true,
			SuggestedStartLocation = await FolderAsync(startFolder),
		};
		var file = await _owner.StorageProvider.SaveFilePickerAsync(options);
		return file?.TryGetLocalPath();
	}

	private async Task<IStorageFolder?> FolderAsync(string? path)
	{
		if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
			return null;
		return await _owner.StorageProvider.TryGetFolderFromPathAsync(path);
	}

	public Task<string?> AskAsync(string title, string message, params string[] buttons) =>
		MessageDialog.ShowAsync(_owner, title, message, buttons);

	public Task ShowMessageAsync(string title, string message) =>
		MessageDialog.ShowAsync(_owner, title, message, "OK");

	public Task<NewDocumentChoice?> ShowNewDocumentAsync(string defaultNamespace) =>
		new NewDocumentDialog(defaultNamespace).ShowDialog<NewDocumentChoice?>(_owner);

	public Task<ExportChoice?> ShowExportAsync(string? startFolder, Func<bool, IReadOnlyList<GeneratedFile>> listFiles) =>
		new ExportDialog(startFolder, listFiles).ShowDialog<ExportChoice?>(_owner);

	public async Task<string?> GetClipboardTextAsync()
	{
		try
		{
			return _owner.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
		}
		catch (Exception)
		{
			return null;
		}
	}

	public async Task SetClipboardTextAsync(string text)
	{
		try
		{
			if (_owner.Clipboard is { } clipboard)
				await clipboard.SetTextAsync(text);
		}
		catch (Exception)
		{
		}
	}

	public void OpenFolder(string folder)
	{
		try
		{
			Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
		}
		catch (Exception)
		{
		}
	}

	public void ShowPreviewWindow(DesignDocument document, ThemeVariant? theme)
	{
		var copy = new DesignDocument(document.Root.Clone(), document.Namespaces) { FilePath = document.FilePath };
		var result = PreviewBuilder.Build(copy, new PreviewOptions
		{
			DesignMode = false,
			ValueContext = Designer.DesignSurface.CreateValueContext(document.FilePath),
		});
		if (result.Root is null)
		{
			_ = ShowMessageAsync("Preview", result.Diagnostics.FirstOrDefault()?.Message ?? "There is nothing to show.");
			return;
		}

		var root = copy.Root;
		string title = root.Get("Title") is { } text && !XamlMarkup.IsMarkup(text) ? text : "Preview";
		var window = new Window
		{
			Title = title + " (preview)",
			Content = result.Root,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
		};
		if (theme is not null)
			window.RequestedThemeVariant = theme;

		double width = result.Root.Width;
		double height = result.Root.Height;
		window.Width = double.IsNaN(width) ? Designer.DesignSurface.DefaultWidth : width;
		window.Height = double.IsNaN(height) ? Designer.DesignSurface.DefaultHeight : height;
		result.Root.Width = double.NaN;
		result.Root.Height = double.NaN;
		if (double.TryParse(root.Get("d:DesignWidth"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double designWidth) && double.IsNaN(width))
			window.Width = designWidth;
		if (double.TryParse(root.Get("d:DesignHeight"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double designHeight) && double.IsNaN(height))
			window.Height = designHeight;

		window.Closed += (_, _) => result.Dispose();
		window.Show(_owner);
	}
}
