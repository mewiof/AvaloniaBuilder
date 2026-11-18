using Avalonia.Styling;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Services;

namespace AvaloniaBuilder.Tests;

public sealed class FakeDialogs : IDialogService
{
	public string? OpenPath { get; set; }

	public string? SavePath { get; set; }

	public string? Answer { get; set; }

	public NewDocumentChoice? NewChoice { get; set; }

	public ExportChoice? ExportChoice { get; set; }

	public string? Clipboard { get; set; }

	public List<string> Questions { get; } = new();

	public List<string> Messages { get; } = new();

	public int PreviewWindows { get; private set; }

	public Task<string?> PickOpenFileAsync(string? startFolder) => Task.FromResult(OpenPath);

	public Task<string?> PickSaveFileAsync(string suggestedName, string? startFolder) => Task.FromResult(SavePath);

	public Task<string?> AskAsync(string title, string message, params string[] buttons)
	{
		Questions.Add(title + ": " + message);
		return Task.FromResult(Answer);
	}

	public Task ShowMessageAsync(string title, string message)
	{
		Messages.Add(title + ": " + message);
		return Task.CompletedTask;
	}

	public Task<NewDocumentChoice?> ShowNewDocumentAsync(string defaultNamespace) => Task.FromResult(NewChoice);

	public Task<ExportChoice?> ShowExportAsync(string? startFolder, Func<bool, IReadOnlyList<GeneratedFile>> listFiles) =>
		Task.FromResult(ExportChoice);

	public Task<string?> GetClipboardTextAsync() => Task.FromResult(Clipboard);

	public Task SetClipboardTextAsync(string text)
	{
		Clipboard = text;
		return Task.CompletedTask;
	}

	public void OpenFolder(string folder)
	{
	}

	public void ShowPreviewWindow(DesignDocument document, ThemeVariant? theme) => PreviewWindows++;
}
