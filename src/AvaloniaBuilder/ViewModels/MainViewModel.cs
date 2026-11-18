using System.Collections.ObjectModel;
using System.Text;
using Avalonia;
using Avalonia.Styling;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Designer;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Preview;
using AvaloniaBuilder.Services;
using AvaloniaBuilder.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaBuilder.ViewModels;

public sealed record ProblemViewModel(DiagnosticLevel Level, string Message, DesignNode? Node)
{
	public string Icon => Level switch
	{
		DiagnosticLevel.Error => "✕",
		DiagnosticLevel.Warning => "!",
		_ => "i",
	};

	public string Where => Node is null ? "" : Node.ToString();
}

public partial class MainViewModel : ObservableObject
{
	public const string AppName = "Avalonia Builder";

	private readonly IDialogService _dialogs;
	private readonly AppSettings _settings;
	private readonly Action<AppSettings>? _saveSettings;
	private DesignDocument? _subscribed;

	private bool _applyingXamlText;

	private bool _xamlTextIsUserText;

	private string? _localClipboard;

	public MainViewModel(IDialogService dialogs, AppSettings? settings = null, Action<AppSettings>? saveSettings = null)
	{
		_dialogs = dialogs;
		_settings = settings ?? new AppSettings();
		_saveSettings = saveSettings;
		UsePartialProperties = _settings.UsePartialProperties;
		UseViewModelBase = _settings.ViewModelBaseClass == "ViewModelBase";
		AppTheme = _settings.AppTheme;
		PreviewThemeName = _settings.PreviewTheme;
		foreach (string file in _settings.RecentFiles)
			RecentFiles.Add(file);
		SetDocument(DocumentTemplates.Create(DocumentTemplates.All[0], _settings.DefaultNamespace));
	}

	public ToolboxViewModel Toolbox { get; } = new();

	public OutlineViewModel Outline { get; } = new();

	public PropertyGridViewModel Properties { get; } = new();

	public ObservableCollection<ProblemViewModel> Problems { get; } = new();

	public ObservableCollection<string> RecentFiles { get; } = new();

	public IReadOnlyList<string> WrapChoices => DesignActions.WrapChoices;

	public event EventHandler? ZoomToFitRequested;

	[ObservableProperty]
	public partial DesignDocument Document { get; private set; } = null!;

	[ObservableProperty]
	public partial DesignNode? SelectedNode { get; private set; }

	[ObservableProperty]
	public partial string Title { get; private set; } = AppName;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ZoomText))]
	public partial double Zoom { get; set; } = 1;

	public string ZoomText => $"{Zoom * 100:0} %";

	[ObservableProperty]
	public partial bool IsRunMode { get; set; }

	[ObservableProperty]
	public partial ThemeVariant? PreviewTheme { get; private set; }

	[ObservableProperty]
	public partial string PreviewThemeName { get; set; } = "Same";

	[ObservableProperty]
	public partial string AppTheme { get; set; } = "Default";

	[ObservableProperty]
	public partial string XamlText { get; private set; } = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasXamlError))]
	public partial string? XamlError { get; private set; }

	public bool HasXamlError => XamlError is not null;

	[ObservableProperty]
	public partial string CodeBehindText { get; private set; } = "";

	[ObservableProperty]
	public partial string ViewModelText { get; private set; } = "";

	[ObservableProperty]
	public partial int SelectedLine { get; private set; }

	[ObservableProperty]
	public partial int SelectedEndLine { get; private set; }

	[ObservableProperty]
	public partial string SelectionPath { get; private set; } = "";

	[ObservableProperty]
	public partial string Message { get; set; } = "";

	[ObservableProperty]
	public partial string ProblemSummary { get; private set; } = "No problems";

	[ObservableProperty]
	public partial bool HasProblems { get; private set; }

	[ObservableProperty]
	public partial bool UsePartialProperties { get; set; } = true;

	[ObservableProperty]
	public partial bool UseViewModelBase { get; set; }

	private CodeGenOptions CodeOptions => new()
	{
		UsePartialProperties = UsePartialProperties,
		ViewModelBaseClass = UseViewModelBase ? "ViewModelBase" : "ObservableObject",
	};

	public void SetDocument(DesignDocument document)
	{
		if (_subscribed is not null)
		{
			_subscribed.Changed -= OnDocumentChanged;
			_subscribed.SelectionChanged -= OnSelectionChanged;
		}
		_subscribed = document;
		document.Changed += OnDocumentChanged;
		document.SelectionChanged += OnSelectionChanged;

		_xamlTextIsUserText = false;
		XamlError = null;
		IsRunMode = false;
		Document = document;
		Outline.SetDocument(document);
		Problems.Clear();
		RegenerateCode();
		if (document.Selected is null)
			document.Select(document.Root);
		OnSelectionChanged(document, EventArgs.Empty);
		UpdateTitle();
	}

	private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
	{
		if (!_applyingXamlText)
			_xamlTextIsUserText = false;
		RegenerateCode();
		Outline.Refresh();
		Properties.SetTarget(Document, Document.Selected);
		UpdateSelectionInfo();
		UpdateTitle();
		RefreshCommands();
	}

	private void OnSelectionChanged(object? sender, EventArgs e)
	{
		SelectedNode = Document.Selected;
		Outline.SyncSelection();
		Properties.SetTarget(Document, Document.Selected);
		UpdateSelectionInfo();
		Message = "";
		RefreshCommands();
	}

	public void OnPreviewUpdated(PreviewResult? preview, string? layoutError)
	{
		Problems.Clear();
		if (layoutError is not null)
			Problems.Add(new ProblemViewModel(DiagnosticLevel.Error, "The preview stopped with an error: " + layoutError, null));
		if (preview is not null)
		{
			foreach (var diagnostic in preview.Diagnostics.OrderByDescending(d => d.Level))
				Problems.Add(new ProblemViewModel(diagnostic.Level, diagnostic.Message, diagnostic.Node));
		}
		int serious = Problems.Count(p => p.Level != DiagnosticLevel.Info);
		HasProblems = serious > 0;
		ProblemSummary = serious == 0
			? (Problems.Count == 0 ? "No problems" : $"{Problems.Count} note{(Problems.Count == 1 ? "" : "s")}")
			: $"{serious} problem{(serious == 1 ? "" : "s")}";

		Outline.SetDiagnostics(preview?.Diagnostics ?? Array.Empty<PreviewDiagnostic>());
		Properties.Refresh();
	}

	private void UpdateTitle()
	{
		string name = Document.FilePath is { } path ? Path.GetFileName(path) : "Untitled.axaml";
		Title = $"{name}{(Document.IsDirty ? "*" : "")} - {AppName}";
	}

	private void UpdateSelectionInfo()
	{
		var selected = Document.Selected;
		if (selected is null)
		{
			SelectionPath = "";
			SelectedLine = 0;
			SelectedEndLine = 0;
			return;
		}
		var names = selected.Ancestors().Reverse().Append(selected).Select(n => n.ElementName);
		SelectionPath = string.Join("  ›  ", names);
		SelectedLine = selected.SourceLine;
		SelectedEndLine = selected.SourceEndLine;
	}

	private void RegenerateCode()
	{
		try
		{
			if (!_applyingXamlText)
				XamlText = DesignXamlWriter.Write(Document);
			CodeBehindText = CodeBehindGenerator.Generate(Document);
			ViewModelText = ViewModelGenerator.Generate(Document, CodeOptions);
		}
		catch (Exception ex)
		{
			Message = "The code could not be generated: " + ex.Message;
		}
	}

	partial void OnUsePartialPropertiesChanged(bool value) => OnCodeOptionChanged();

	partial void OnUseViewModelBaseChanged(bool value) => OnCodeOptionChanged();

	private void OnCodeOptionChanged()
	{
		if (_subscribed is null)
			return;
		_settings.UsePartialProperties = UsePartialProperties;
		_settings.ViewModelBaseClass = UseViewModelBase ? "ViewModelBase" : "ObservableObject";
		_saveSettings?.Invoke(_settings);
		ViewModelText = ViewModelGenerator.Generate(Document, CodeOptions);
	}

	public void ApplyXamlText(string text)
	{
		text = text.Replace("\r\n", "\n");
		if (text == XamlText)
		{
			return;
		}

		DesignDocument parsed;
		try
		{
			parsed = DesignXamlReader.Parse(text);
		}
		catch (XamlReadException ex)
		{
			XamlError = $"Line {ex.Line}: {ex.Message}";
			return;
		}

		XamlError = null;
		_applyingXamlText = true;
		_xamlTextIsUserText = true;
		try
		{
			XamlText = text;
			Document.ReplaceContent(parsed.Root, parsed.Namespaces);
		}
		finally
		{
			_applyingXamlText = false;
		}
	}

	public void SelectNodeAtLine(int line)
	{
		DesignNode? best = null;
		foreach (var node in Document.Root.SelfAndDescendants())
		{
			if (node.SourceLine <= line && line <= node.SourceEndLine
				&& (best is null || node.SourceEndLine - node.SourceLine <= best.SourceEndLine - best.SourceLine))
				best = node;
		}
		if (best is not null && best != Document.Selected)
			Document.Select(best);
	}

	[RelayCommand]
	private async Task CopyCodeAsync(string which)
	{
		string text = which switch
		{
			"xaml" => XamlText,
			"codebehind" => CodeBehindText,
			_ => ViewModelText,
		};
		await _dialogs.SetClipboardTextAsync(text.Replace("\n", Environment.NewLine));
		Message = "Copied to the clipboard.";
	}

	public async Task<bool> ConfirmDiscardAsync()
	{
		if (!Document.IsDirty)
			return true;
		string name = Document.FilePath is { } path ? Path.GetFileName(path) : "the new document";
		string? answer = await _dialogs.AskAsync("Unsaved changes", $"Do you want to save the changes to {name}?", "Save", "Don't save", "Cancel");
		if (answer == "Save")
			return await SaveAsync();
		return answer == "Don't save";
	}

	[RelayCommand]
	private async Task NewAsync()
	{
		if (!await ConfirmDiscardAsync())
			return;
		var choice = await _dialogs.ShowNewDocumentAsync(_settings.DefaultNamespace);
		if (choice is null)
			return;
		_settings.DefaultNamespace = choice.RootNamespace;
		_saveSettings?.Invoke(_settings);
		SetDocument(DocumentTemplates.Create(choice.Template, choice.RootNamespace, choice.ViewName));
		ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
	}

	[RelayCommand]
	private async Task OpenAsync()
	{
		if (!await ConfirmDiscardAsync())
			return;
		string? path = await _dialogs.PickOpenFileAsync(_settings.LastFolder);
		if (path is not null)
			await OpenFileAsync(path);
	}

	[RelayCommand]
	private async Task OpenRecentAsync(string path)
	{
		if (await ConfirmDiscardAsync())
			await OpenFileAsync(path);
	}

	public async Task<bool> OpenFileAsync(string path)
	{
		DesignDocument document;
		try
		{
			string text = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
			document = DesignXamlReader.Parse(text);
		}
		catch (XamlReadException ex)
		{
			await _dialogs.ShowMessageAsync("Cannot open the file", $"{Path.GetFileName(path)} is not valid XAML.\n\nLine {ex.Line}: {ex.Message}");
			return false;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await _dialogs.ShowMessageAsync("Cannot open the file", ex.Message);
			RecentFiles.Remove(path);
			_settings.RecentFiles.Remove(path);
			_saveSettings?.Invoke(_settings);
			return false;
		}

		document.FilePath = path;
		document.MarkSaved();
		SetDocument(document);
		Remember(path);
		ZoomToFitRequested?.Invoke(this, EventArgs.Empty);
		return true;
	}

	[RelayCommand]
	private async Task<bool> SaveAsync()
	{
		if (Document.FilePath is null)
			return await SaveAsAsync();
		return await WriteFileAsync(Document.FilePath);
	}

	[RelayCommand]
	private async Task<bool> SaveAsAsync()
	{
		string suggested = Document.FilePath is { } current
			? Path.GetFileName(current)
			: CodeNames.Split(Document.ViewClass ?? ViewModelGenerator.DefaultViewClass).Name + ".axaml";
		string? path = await _dialogs.PickSaveFileAsync(suggested, _settings.LastFolder);
		if (path is null)
			return false;
		return await WriteFileAsync(path);
	}

	private async Task<bool> WriteFileAsync(string path)
	{
		try
		{
			string text = _xamlTextIsUserText ? XamlText : DesignXamlWriter.Write(Document);
			await File.WriteAllTextAsync(path, text.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await _dialogs.ShowMessageAsync("Cannot save the file", ex.Message);
			return false;
		}
		Document.FilePath = path;
		Document.MarkSaved();
		Remember(path);
		UpdateTitle();
		Message = "Saved " + Path.GetFileName(path);
		return true;
	}

	private void Remember(string path)
	{
		_settings.AddRecentFile(path);
		_settings.LastFolder = Path.GetDirectoryName(path);
		_saveSettings?.Invoke(_settings);
		RecentFiles.Clear();
		foreach (string file in _settings.RecentFiles)
			RecentFiles.Add(file);
	}

	[RelayCommand]
	private async Task ExportAsync()
	{
		IReadOnlyList<GeneratedFile> Files(bool createProject) =>
			ProjectExporter.Generate(Document, new ExportOptions { CreateProject = createProject, CodeGen = CodeOptions });

		var choice = await _dialogs.ShowExportAsync(_settings.LastExportFolder, Files);
		if (choice is null)
			return;

		ExportResult result;
		try
		{
			result = ProjectExporter.Write(Files(choice.CreateProject), choice.Folder, choice.Overwrite);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await _dialogs.ShowMessageAsync("Export failed", ex.Message);
			return;
		}

		_settings.LastExportFolder = choice.Folder;
		_saveSettings?.Invoke(_settings);

		var text = new StringBuilder();
		text.Append($"{result.Written.Count} file{(result.Written.Count == 1 ? "" : "s")} written to\n{choice.Folder}\n");
		foreach (string file in result.Written)
			text.Append("\n   ").Append(file);
		if (result.Skipped.Count > 0)
		{
			text.Append("\n\nNot written, because the file is there already:");
			foreach (string file in result.Skipped)
				text.Append("\n   ").Append(file);
		}
		if (choice.CreateProject && result.Written.Count > 0)
			text.Append("\n\nTo run it, open a terminal in that folder and type:  dotnet run");

		string? answer = await _dialogs.AskAsync("Export finished", text.ToString(), "Open folder", "Close");
		if (answer == "Open folder")
			_dialogs.OpenFolder(choice.Folder);
		Message = $"Exported {result.Written.Count} file{(result.Written.Count == 1 ? "" : "s")}.";
	}

	private bool CanUndo() => Document.CanUndo;

	private bool CanRedo() => Document.CanRedo;

	private bool HasElement() => Document.Selected is { Parent: not null };

	private bool HasSelection() => Document.Selected is not null;

	private void RefreshCommands()
	{
		UndoCommand.NotifyCanExecuteChanged();
		RedoCommand.NotifyCanExecuteChanged();
		CutCommand.NotifyCanExecuteChanged();
		CopyCommand.NotifyCanExecuteChanged();
		DeleteCommand.NotifyCanExecuteChanged();
		DuplicateCommand.NotifyCanExecuteChanged();
		MoveEarlierCommand.NotifyCanExecuteChanged();
		MoveLaterCommand.NotifyCanExecuteChanged();
		SelectParentCommand.NotifyCanExecuteChanged();
		WrapCommand.NotifyCanExecuteChanged();
		UnwrapCommand.NotifyCanExecuteChanged();
		AddGridRowCommand.NotifyCanExecuteChanged();
		AddGridColumnCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand(CanExecute = nameof(CanUndo))]
	private void Undo() => Document.Undo();

	[RelayCommand(CanExecute = nameof(CanRedo))]
	private void Redo() => Document.Redo();

	[RelayCommand(CanExecute = nameof(HasElement))]
	private void Delete()
	{
		if (Document.Selected is { Parent: not null } node)
			Document.RemoveNode(node);
	}

	[RelayCommand(CanExecute = nameof(HasSelection))]
	private async Task CopyAsync()
	{
		if (Document.Selected is not { } node)
			return;
		_localClipboard = DesignActions.CopyText(Document, node);
		await _dialogs.SetClipboardTextAsync(_localClipboard.Replace("\n", Environment.NewLine));
		Message = $"Copied <{node.ElementName}>. You can paste it here or into any text editor.";
	}

	[RelayCommand(CanExecute = nameof(HasElement))]
	private async Task CutAsync()
	{
		if (Document.Selected is not { Parent: not null } node)
			return;
		await CopyAsync();
		Document.RemoveNode(node);
	}

	[RelayCommand]
	private async Task PasteAsync()
	{
		string? text = await _dialogs.GetClipboardTextAsync();
		if (string.IsNullOrWhiteSpace(text))
			text = _localClipboard;
		if (string.IsNullOrWhiteSpace(text))
		{
			Message = "The clipboard is empty.";
			return;
		}
		string? error = DesignActions.Paste(Document, text.Replace("\r\n", "\n"));
		Message = error ?? "Pasted.";
	}

	[RelayCommand(CanExecute = nameof(HasElement))]
	private void Duplicate()
	{
		if (Document.Selected is { Parent: not null } node)
			Document.Duplicate(node);
	}

	[RelayCommand(CanExecute = nameof(HasElement))]
	private void MoveEarlier()
	{
		if (Document.Selected is { } node)
			Document.MoveInParent(node, -1);
	}

	[RelayCommand(CanExecute = nameof(HasElement))]
	private void MoveLater()
	{
		if (Document.Selected is { } node)
			Document.MoveInParent(node, 1);
	}

	[RelayCommand(CanExecute = nameof(HasElement))]
	private void SelectParent()
	{
		if (Document.Selected?.ObjectParent is { } parent)
			Document.Select(parent);
	}

	private bool CanWrap(string? type) => DesignActions.CanWrap(Document, Document.Selected);

	[RelayCommand(CanExecute = nameof(CanWrap))]
	private void Wrap(string? type)
	{
		if (type is null || Document.Selected is not { } node || !DesignActions.CanWrap(Document, node))
			return;
		Document.Wrap(node, DesignActions.MakeWrapper(type));
	}

	private bool CanUnwrap() => DesignActions.CanUnwrap(Document, Document.Selected);

	[RelayCommand(CanExecute = nameof(CanUnwrap))]
	private void Unwrap()
	{
		if (Document.Selected is { } node && DesignActions.CanUnwrap(Document, node))
			Document.Unwrap(node);
	}

	private DesignNode? GridOfSelection()
	{
		if (Document.Selected is not { } node)
			return null;
		if (node.TypeName == "Grid" && !node.IsPropertyElement)
			return node;
		return node.ObjectParent is { TypeName: "Grid" } parent ? parent : null;
	}

	private bool HasGrid() => GridOfSelection() is not null;

	[RelayCommand(CanExecute = nameof(HasGrid))]
	private void AddGridRow() => AddGridTrack("RowDefinitions", "row");

	[RelayCommand(CanExecute = nameof(HasGrid))]
	private void AddGridColumn() => AddGridTrack("ColumnDefinitions", "column");

	private void AddGridTrack(string attribute, string word)
	{
		if (GridOfSelection() is not { } grid)
			return;
		if (grid.Children.Any(c => c.IsPropertyElement && c.PropertyName == attribute))
		{
			Message = $"This Grid has its {word}s as elements (<Grid.{attribute}>). Change them in the XAML tab.";
			return;
		}
		string current = (grid.Get(attribute) ?? "").Trim().TrimEnd(',');
		string updated = current.Length == 0 ? "*,*" : current + ",*";
		Document.SetAttribute(grid, attribute, updated);
		Message = $"The Grid has a new {word} with the size * (a share of the free space). Change the size in the property panel.";
	}

	[RelayCommand]
	private void AddFromToolbox(ToolboxItem? item)
	{
		if (item is null)
			return;
		List<DesignNode> nodes;
		List<XamlNamespace> namespaces;
		try
		{
			nodes = item.Create(Document, out namespaces);
		}
		catch (XamlReadException ex)
		{
			Message = ex.Message;
			return;
		}
		if (nodes.Count == 0)
			return;
		var place = DesignActions.FindInsertionPoint(Document, nodes[0]);
		if (place is null)
		{
			Message = $"There is no place for a {item.Name} here. Select a panel first.";
			return;
		}
		DesignActions.Insert(Document, place.Value.Parent, place.Value.Index, nodes, namespaces);
		Message = $"Added {item.Name} to <{place.Value.Parent.ElementName}>.";
	}

	[RelayCommand]
	private void SelectProblem(ProblemViewModel? problem)
	{
		if (problem?.Node is { } node)
			Document.Select(node);
	}

	[RelayCommand]
	private void ToggleRunMode() => IsRunMode = !IsRunMode;

	[RelayCommand]
	private void OpenPreviewWindow() => _dialogs.ShowPreviewWindow(Document, PreviewTheme);

	[RelayCommand]
	private void ZoomIn() => Zoom = Math.Min(DesignSurface.MaxZoom, Math.Round(Zoom * 1.25, 2));

	[RelayCommand]
	private void ZoomOut() => Zoom = Math.Max(DesignSurface.MinZoom, Math.Round(Zoom / 1.25, 2));

	[RelayCommand]
	private void ZoomReset() => Zoom = 1;

	[RelayCommand]
	private void ZoomToFit() => ZoomToFitRequested?.Invoke(this, EventArgs.Empty);

	[RelayCommand]
	private void SetPreviewTheme(string name) => PreviewThemeName = name;

	partial void OnPreviewThemeNameChanged(string value)
	{
		PreviewTheme = value switch
		{
			"Light" => ThemeVariant.Light,
			"Dark" => ThemeVariant.Dark,
			_ => null,
		};
		if (_subscribed is null)
			return;
		_settings.PreviewTheme = value;
		_saveSettings?.Invoke(_settings);
	}

	[RelayCommand]
	private void SetAppTheme(string name) => AppTheme = name;

	partial void OnAppThemeChanged(string value)
	{
		if (Application.Current is { } app)
		{
			app.RequestedThemeVariant = value switch
			{
				"Light" => ThemeVariant.Light,
				"Dark" => ThemeVariant.Dark,
				_ => ThemeVariant.Default,
			};
		}
		if (_subscribed is null)
			return;
		_settings.AppTheme = value;
		_saveSettings?.Invoke(_settings);
	}
}
