using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Preview;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.Designer;

public partial class DesignSurface : UserControl
{
	public static readonly StyledProperty<DesignDocument?> DocumentProperty =
		AvaloniaProperty.Register<DesignSurface, DesignDocument?>(nameof(Document));

	public static readonly StyledProperty<double> ZoomProperty =
		AvaloniaProperty.Register<DesignSurface, double>(nameof(Zoom), 1.0, defaultBindingMode: BindingMode.TwoWay,
			coerce: (_, value) => Math.Clamp(double.IsNaN(value) ? 1 : value, MinZoom, MaxZoom));

	public static readonly StyledProperty<bool> IsRunModeProperty =
		AvaloniaProperty.Register<DesignSurface, bool>(nameof(IsRunMode));

	public static readonly StyledProperty<ThemeVariant?> PreviewThemeProperty =
		AvaloniaProperty.Register<DesignSurface, ThemeVariant?>(nameof(PreviewTheme));

	public const double MinZoom = 0.1;
	public const double MaxZoom = 4;

	public const double DefaultWidth = 800;
	public const double DefaultHeight = 450;

	private const double ZoomStep = 1.1;
	private const double ZoomSnap = 0.005;
	private const double WheelStep = 50;
	private const double KeepVisible = 48;

	private static readonly Point HomePan = new(40, 40);

	private readonly Dictionary<DesignNode, int> _activeTabs = new();
	private HashSet<DesignNode> _forcedOpen = new();
	private DesignDocument? _document;
	private PreviewResult? _preview;
	private string? _layoutError;
	private Point _pan = HomePan;

	public DesignSurface()
	{
		InitializeComponent();

		Overlay.Painter = Paint;
		Overlay.PointerPressed += OnOverlayPointerPressed;
		Overlay.PointerMoved += OnOverlayPointerMoved;
		Overlay.PointerReleased += OnOverlayPointerReleased;
		Overlay.PointerCaptureLost += OnOverlayPointerCaptureLost;
		Overlay.PointerExited += OnOverlayPointerExited;
		Overlay.KeyDown += OnOverlayKeyDown;
		Overlay.DoubleTapped += OnOverlayDoubleTapped;
		AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
		AddHandler(PointerWheelChangedEvent, OnWheelPan, RoutingStrategies.Bubble);
		AddHandler(PointerPressedEvent, OnPanPressed, RoutingStrategies.Tunnel);
		AddHandler(PointerMovedEvent, OnPanMoved, RoutingStrategies.Tunnel);
		AddHandler(PointerReleasedEvent, OnPanReleased, RoutingStrategies.Tunnel);
		SizeChanged += (_, _) => SetPan(_pan);

		PreviewHost.LayoutUpdated += (_, _) => Overlay.InvalidateVisual();
		PreviewHost.LayoutFailed += OnLayoutFailed;
		PreviewHost.IsHitTestVisible = false;
	}

	public DesignDocument? Document
	{
		get => GetValue(DocumentProperty);
		set => SetValue(DocumentProperty, value);
	}

	public double Zoom
	{
		get => GetValue(ZoomProperty);
		set => SetValue(ZoomProperty, value);
	}

	public bool IsRunMode
	{
		get => GetValue(IsRunModeProperty);
		set => SetValue(IsRunModeProperty, value);
	}

	public ThemeVariant? PreviewTheme
	{
		get => GetValue(PreviewThemeProperty);
		set => SetValue(PreviewThemeProperty, value);
	}

	public PreviewResult? Preview => _preview;

	public event EventHandler? PreviewChanged;

	public event EventHandler<DesignNode>? EditRequested;

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		base.OnPropertyChanged(change);
		if (change.Property == DocumentProperty)
		{
			Attach(change.GetNewValue<DesignDocument?>());
		}
		else if (change.Property == ZoomProperty)
		{
			ZoomHost.LayoutTransform = new ScaleTransform(Zoom, Zoom);
			SetPan(_pan);
		}
		else if (change.Property == IsRunModeProperty)
		{
			CancelDrag();
			PreviewHost.IsHitTestVisible = IsRunMode;
			Rebuild();
		}
		else if (change.Property == PreviewThemeProperty)
		{
			ThemeScope.RequestedThemeVariant = PreviewTheme;
		}
	}

	private void Attach(DesignDocument? document)
	{
		if (_document is not null)
		{
			_document.Changed -= OnDocumentChanged;
			_document.SelectionChanged -= OnSelectionChanged;
		}
		_document = document;
		_activeTabs.Clear();
		CancelDrag();
		if (_document is not null)
		{
			_document.Changed += OnDocumentChanged;
			_document.SelectionChanged += OnSelectionChanged;
		}
		Rebuild();
		SetPan(HomePan);
	}

	private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
	{
		if (e.Kind == ChangeKind.Reset)
			_activeTabs.Clear();
		Rebuild();
	}

	private void OnSelectionChanged(object? sender, EventArgs e)
	{
		if (SelectionNeedsRebuild())
			Rebuild();
		else
			Overlay.InvalidateVisual();
	}

	public void Rebuild()
	{
		_mode = DragMode.None;
		_dropTarget = null;
		_hover = null;
		_layoutError = null;

		var old = _preview;
		_preview = null;
		PreviewHost.Child = null;
		old?.Dispose();

		if (_document is null)
		{
			ZoomHost.IsVisible = false;
			Overlay.IsVisible = false;
			EmptyMessage.IsVisible = false;
			PreviewChanged?.Invoke(this, EventArgs.Empty);
			return;
		}

		var selected = _document.Selected;
		_preview = PreviewBuilder.Build(_document, new PreviewOptions
		{
			DesignMode = !IsRunMode,
			Selected = selected,
			ActiveTabs = _activeTabs,
			ValueContext = CreateValueContext(_document.FilePath),
		});
		_forcedOpen = ForcedOpenFor(selected);
		PreviewHost.Child = _preview.Root;
		UpdateFrame();
		Overlay.InvalidateVisual();
		PreviewChanged?.Invoke(this, EventArgs.Empty);
	}

	public static ValueContext? CreateValueContext(string? filePath)
	{
		if (string.IsNullOrEmpty(filePath))
			return null;
		string? folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
		string? project = folder;
		while (project is not null && !Directory.EnumerateFiles(project, "*.csproj").Any())
			project = Path.GetDirectoryName(project);
		return new ValueContext { DocumentDirectory = folder, ProjectDirectory = project ?? folder };
	}

	private void OnLayoutFailed(object? sender, string message)
	{
		_layoutError = message;
		UpdateFrame();
		PreviewChanged?.Invoke(this, EventArgs.Empty);
	}

	public string? LayoutError => _layoutError;

	private void UpdateFrame()
	{
		if (_document is null)
			return;
		var root = _document.Root;
		bool hasVisual = _preview?.Root is not null && _layoutError is null;

		ZoomHost.IsVisible = hasVisual;
		Overlay.IsVisible = hasVisual && !IsRunMode;
		EmptyMessage.IsVisible = !hasVisual;
		if (!hasVisual)
		{
			EmptyMessage.Text = _layoutError is not null
				? "The preview stopped with an error:\n" + _layoutError
				: _preview?.Diagnostics.FirstOrDefault()?.Message ?? "There is nothing to show.";
			return;
		}

		TitleBar.IsVisible = DesignRules.IsWindow(XamlTypes.Resolve(_document, root));
		string title = root.Get("Title") ?? "";
		TitleText.Text = XamlMarkup.Parse(title) is { IsBinding: true } binding ? "{" + binding.BindingPath + "}" : title;

		var size = GetDesignSize();
		ClientArea.Width = size.Width;
		ClientArea.Height = size.Height;
	}

	public Size GetDesignSize()
	{
		if (_document is null)
			return new Size(DefaultWidth, DefaultHeight);
		var root = _document.Root;
		double width = ParseNumber(root.Get("Width")) ?? ParseNumber(root.Get("d:DesignWidth")) ?? DefaultWidth;
		double height = ParseNumber(root.Get("Height")) ?? ParseNumber(root.Get("d:DesignHeight")) ?? DefaultHeight;
		return new Size(width, height);
	}

	private static double? ParseNumber(string? text) =>
		double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0 && !double.IsInfinity(value)
			? value
			: null;

	private static HashSet<DesignNode> ForcedOpenFor(DesignNode? selected)
	{
		var forced = new HashSet<DesignNode>();
		for (var node = selected?.Parent; node is not null; node = node.Parent)
			if (node.TypeName is "Expander" or "TreeViewItem")
				forced.Add(node);
		return forced;
	}

	private bool SelectionNeedsRebuild()
	{
		if (_document is null || IsRunMode)
			return false;
		var selected = _document.Selected;
		if (!ForcedOpenFor(selected).SetEquals(_forcedOpen))
			return true;

		for (var node = selected; node?.Parent is { } parent; node = parent)
		{
			if (parent.TypeName != "TabControl" || node.IsPropertyElement)
				continue;
			int index = parent.ContentIndexOf(node);
			int shown = _activeTabs.TryGetValue(parent, out int active) ? active
				: int.TryParse(parent.Get("SelectedIndex"), out int fromAttribute) ? fromAttribute
				: 0;
			if (index != shown)
				return true;
		}
		return false;
	}

	public void ZoomToFit()
	{
		var size = GetDesignSize();
		double titleBar = TitleBar.IsVisible ? TitleBar.Height : 0;
		double availableWidth = Bounds.Width - 100;
		double availableHeight = Bounds.Height - 100;
		if (availableWidth <= 0 || availableHeight <= 0)
			return;
		Zoom = Math.Min(1, Math.Min(availableWidth / size.Width, availableHeight / (size.Height + titleBar)));
		SetPan(HomePan);
	}

	internal Point Pan => _pan;

	private Size FrameSize()
	{
		var size = GetDesignSize();
		double titleBar = TitleBar.IsVisible ? TitleBar.Height : 0;
		return new Size(size.Width * Zoom, (size.Height + titleBar) * Zoom);
	}

	private void SetPan(Point pan)
	{
		var frame = FrameSize();
		_pan = new Point(
			ClampPan(pan.X, frame.Width, Bounds.Width),
			ClampPan(pan.Y, frame.Height, Bounds.Height));
		Canvas.SetLeft(ZoomHost, _pan.X);
		Canvas.SetTop(ZoomHost, _pan.Y);
		Overlay.InvalidateVisual();
	}

	private static double ClampPan(double value, double frame, double view)
	{
		if (view <= 0)
			return value;
		double keep = Math.Min(KeepVisible, Math.Min(frame, view));
		return Math.Clamp(value, keep - frame, view - keep);
	}

	private void OnWheel(object? sender, PointerWheelEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
			return;
		e.Handled = true;
		if (e.Delta.Y == 0)
			return;

		double oldZoom = Zoom;
		double newZoom = Math.Clamp(oldZoom * (e.Delta.Y > 0 ? ZoomStep : 1 / ZoomStep), MinZoom, MaxZoom);
		bool passesFullSize = (oldZoom < 1 - ZoomSnap && newZoom > 1) || (oldZoom > 1 + ZoomSnap && newZoom < 1);
		if (passesFullSize || Math.Abs(newZoom - 1) < ZoomSnap)
			newZoom = 1;
		if (newZoom == oldZoom)
			return;

		var cursor = e.GetPosition(this);
		var inDesign = (cursor - _pan) / oldZoom;
		Zoom = newZoom;
		SetPan(cursor - inDesign * Zoom);
	}

	private void OnWheelPan(object? sender, PointerWheelEventArgs e)
	{
		if (e.Handled || e.KeyModifiers.HasFlag(KeyModifiers.Control))
			return;
		var delta = e.Delta;
		if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && delta.X == 0)
			delta = new Vector(delta.Y, 0);
		SetPan(_pan + delta * WheelStep);
		e.Handled = true;
	}

	private Control? ControlOf(DesignNode node)
	{
		var control = _preview?.GetControl(node);
		if (control is null || !control.IsEffectivelyVisible || !ReferenceEquals(TopLevel.GetTopLevel(control), TopLevel.GetTopLevel(Overlay)))
			return null;
		return control;
	}

	private Rect RootBounds()
	{
		var matrix = ClientArea.TransformToVisual(Overlay);
		return matrix is null ? default : new Rect(ClientArea.Bounds.Size).TransformToAABB(matrix.Value);
	}

	internal Rect? BoundsOf(DesignNode node)
	{
		if (_document is null)
			return null;
		if (node == _document.Root)
			return RootBounds();
		var control = ControlOf(node);
		var matrix = control?.TransformToVisual(Overlay);
		if (matrix is null)
			return null;
		return new Rect(control!.Bounds.Size).TransformToAABB(matrix.Value);
	}

	internal Rect? VisibleBoundsOf(DesignNode node)
	{
		if (BoundsOf(node) is not { } rect)
			return null;
		if (_document is not null && node != _document.Root && ControlOf(node) is { } control)
		{
			foreach (var ancestor in control.GetVisualAncestors())
			{
				if (ReferenceEquals(ancestor, PreviewHost))
					break;
				if (ancestor.ClipToBounds && ancestor.TransformToVisual(Overlay) is { } matrix)
					rect = rect.Intersect(new Rect(ancestor.Bounds.Size).TransformToAABB(matrix));
			}
		}
		rect = rect.Intersect(RootBounds());
		return rect.Width > 0 && rect.Height > 0 ? rect : null;
	}

	internal DesignNode? HitTest(Point point, DesignNode? exclude = null)
	{
		if (_document is null || _preview is null)
			return null;
		var hit = HitTest(_document.Root, point, exclude);
		return hit;
	}

	private DesignNode? HitTest(DesignNode node, Point point, DesignNode? exclude)
	{
		if (node == exclude)
			return null;
		for (int i = node.Children.Count - 1; i >= 0; i--)
		{
			var hit = HitTest(node.Children[i], point, exclude);
			if (hit is not null)
				return hit;
		}
		if (node.IsPropertyElement)
			return null;
		return VisibleBoundsOf(node) is { } rect && rect.Contains(point) ? node : null;
	}

	public object? GetLiveValue(DesignNode node, PropertyMeta property)
	{
		if (_preview is null || !_preview.Objects.TryGetValue(node, out var instance))
			return null;
		try
		{
			if (property.IsAttached || property.DeclaringType.IsInstanceOfType(instance))
				return property.GetValue(instance);
		}
		catch (Exception)
		{
		}
		return null;
	}
}
