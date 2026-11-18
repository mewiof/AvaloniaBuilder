using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaBuilder.Services;
using AvaloniaBuilder.ViewModels;
using AvaloniaEdit.Editing;

namespace AvaloniaBuilder.Views;

public partial class MainWindow : Window
{
	private const string HelpText =
		"BUILD A VIEW\n" +
		"  1. Drag an entry from the toolbox onto the design. Or double-click it.\n" +
		"  2. A green frame shows which panel takes it. Hold Shift to drop into the panel one level higher.\n" +
		"  3. Click an element to select it. Change its properties on the right.\n" +
		"  4. The tabs at the bottom show the XAML, the code-behind and the view model. They follow every change.\n" +
		"  5. \"Export code\" writes the files, or a whole project that runs.\n\n" +
		"BIND A PROPERTY\n" +
		"  Click the { } button of a property and type the name, for example Speed.\n" +
		"  The XAML gets {Binding Speed}, and the view model gets a Speed property with the right type.\n" +
		"  For a button, bind Command to a name like SaveCommand. The view model gets a Save() method.\n\n" +
		"MOUSE\n" +
		"  Drag an element                move it (to another panel, another grid cell, another place in the row)\n" +
		"  Drag a small square            change the size\n" +
		"  Double-click                   edit the main property\n" +
		"  Right-click                    menu (wrap in a Border, duplicate, ...)\n" +
		"  Ctrl + wheel                   zoom\n" +
		"  Middle button + drag           move the view\n\n" +
		"KEYBOARD\n" +
		"  Delete                         delete the element\n" +
		"  Ctrl+Z, Ctrl+Y                 undo, redo\n" +
		"  Ctrl+C, Ctrl+X, Ctrl+V         copy, cut, paste (the clipboard holds XAML text)\n" +
		"  Ctrl+D                         duplicate\n" +
		"  Escape                         select the parent\n" +
		"  Arrow keys                     go to the next element (in a Canvas: move by 1 pixel, with Shift 10)\n" +
		"  Alt+Up, Alt+Down               move earlier or later among the siblings\n" +
		"  F5                             run mode: try the controls\n" +
		"  Ctrl+F5                        preview in a real window";

	private readonly MainViewModel _viewModel;
	private bool _closeConfirmed;

	public MainWindow() : this(null)
	{
	}

	public MainWindow(MainViewModel? viewModel)
	{
		InitializeComponent();
		PreviewThemeBox.ItemsSource = new[] { "Same", "Light", "Dark" };

		if (viewModel is null)
		{
			var settings = SettingsStore.Load();
			viewModel = new MainViewModel(new DialogService(this), settings, s => SettingsStore.Save(s));
		}
		_viewModel = viewModel;
		DataContext = _viewModel;

		_viewModel.Properties.LiveValueProvider = Surface.GetLiveValue;
		Surface.PreviewChanged += (_, _) => _viewModel.OnPreviewUpdated(Surface.Preview, Surface.LayoutError);
		_viewModel.OnPreviewUpdated(Surface.Preview, Surface.LayoutError);
		Surface.EditRequested += (_, _) => PropertiesPanel.FocusMainEditor();
		_viewModel.ZoomToFitRequested += (_, _) => Dispatcher.UIThread.Post(Surface.ZoomToFit, DispatcherPriority.Loaded);

		ToolboxPanel.DragStarted += item => Surface.BeginToolDrag(item);
		ToolboxPanel.DragMoved += e => Surface.UpdateToolDrag(e);
		ToolboxPanel.DragCompleted += () => Surface.CompleteToolDrag();
		ToolboxPanel.DragCanceled += () => Surface.CancelDrag();
		ToolboxPanel.ItemActivated += item => _viewModel.AddFromToolboxCommand.Execute(item);

		_viewModel.RecentFiles.CollectionChanged += OnRecentFilesChanged;
		RebuildRecentMenu();

		AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
		Opened += (_, _) => Dispatcher.UIThread.Post(() =>
		{
			Surface.ZoomToFit();
			Surface.Focus();
		}, DispatcherPriority.Loaded);
	}

	public MainViewModel ViewModel => _viewModel;

	public void OpenFile(string path) => _ = _viewModel.OpenFileAsync(path);

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Handled)
			return;
		if (e.Source is Avalonia.Visual source
			&& (source.FindAncestorOfType<TextBox>(includeSelf: true) is not null
				|| source.FindAncestorOfType<TextArea>(includeSelf: true) is not null))
			return;

		bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
		bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
		bool inDesignArea = e.Source is Avalonia.Visual visual
			&& (visual.FindAncestorOfType<Designer.DesignSurface>(includeSelf: true) is not null
				|| visual.FindAncestorOfType<OutlineView>(includeSelf: true) is not null);
		System.Windows.Input.ICommand? command = e.Key switch
		{
			Key.Delete when inDesignArea => _viewModel.DeleteCommand,
			Key.Z when ctrl && shift => _viewModel.RedoCommand,
			Key.Z when ctrl => _viewModel.UndoCommand,
			Key.Y when ctrl => _viewModel.RedoCommand,
			Key.C when ctrl => _viewModel.CopyCommand,
			Key.X when ctrl => _viewModel.CutCommand,
			Key.V when ctrl => _viewModel.PasteCommand,
			Key.D when ctrl => _viewModel.DuplicateCommand,
			Key.F1 => null,
			_ => null,
		};

		if (e.Key == Key.F1)
		{
			ShowHelp();
			e.Handled = true;
			return;
		}
		if (command is null)
			return;
		if (_viewModel.IsRunMode)
			return;
		if (command.CanExecute(null))
			command.Execute(null);
		e.Handled = true;
	}

	private void OnRecentFilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRecentMenu();

	private void RebuildRecentMenu()
	{
		var items = new List<MenuItem>();
		foreach (string path in _viewModel.RecentFiles)
		{
			items.Add(new MenuItem
			{
				Header = new TextBlock { Text = path },
				Command = _viewModel.OpenRecentCommand,
				CommandParameter = path,
			});
		}
		RecentMenu.ItemsSource = items;
		RecentMenu.IsEnabled = items.Count > 0;
	}

	private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

	private void OnProblemsClick(object? sender, RoutedEventArgs e) => CodePanel.ShowProblems();

	private void OnHelpClick(object? sender, RoutedEventArgs e) => ShowHelp();

	private void ShowHelp()
	{
		var dialog = new Window
		{
			Title = "How to use " + MainViewModel.AppName,
			Width = 760,
			Height = 620,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ShowInTaskbar = false,
			Content = new ScrollViewer
			{
				Padding = new Avalonia.Thickness(20),
				Content = new SelectableTextBlock
				{
					Text = HelpText,
					FontFamily = (Avalonia.Media.FontFamily)this.FindResource("CodeFont")!,
					FontSize = 13,
					LineHeight = 21,
				},
			},
		};
		dialog.Show(this);
	}

	private async void OnAboutClick(object? sender, RoutedEventArgs e)
	{
		await MessageDialog.ShowAsync(this, "About",
			MainViewModel.AppName + "\n\n" +
			"A visual designer for Avalonia views.\n" +
			"You drag controls onto a window. The designer writes the XAML, the code-behind and the view model.\n\n" +
			"The preview uses the Avalonia version of this program: " + typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3));
	}

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		base.OnClosing(e);
		if (_closeConfirmed || !_viewModel.Document.IsDirty)
			return;

		e.Cancel = true;
		Dispatcher.UIThread.Post(async () =>
		{
			CodePanel.ApplyNow();
			if (await _viewModel.ConfirmDiscardAsync())
			{
				_closeConfirmed = true;
				Close();
			}
		});
	}
}
