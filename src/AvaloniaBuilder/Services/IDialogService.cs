using Avalonia.Styling;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Services;

public sealed record NewDocumentChoice(DocumentTemplate Template, string RootNamespace, string ViewName);

public sealed record ExportChoice(string Folder, bool CreateProject, bool Overwrite);

public interface IDialogService
{
	Task<string?> PickOpenFileAsync(string? startFolder);

	Task<string?> PickSaveFileAsync(string suggestedName, string? startFolder);

	Task<string?> AskAsync(string title, string message, params string[] buttons);

	Task ShowMessageAsync(string title, string message);

	Task<NewDocumentChoice?> ShowNewDocumentAsync(string defaultNamespace);

	Task<ExportChoice?> ShowExportAsync(string? startFolder, Func<bool, IReadOnlyList<GeneratedFile>> listFiles);

	Task<string?> GetClipboardTextAsync();

	Task SetClipboardTextAsync(string text);

	void OpenFolder(string folder);

	void ShowPreviewWindow(DesignDocument document, ThemeVariant? theme);
}
