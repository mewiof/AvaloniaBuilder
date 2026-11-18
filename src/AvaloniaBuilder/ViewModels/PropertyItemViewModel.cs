using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaBuilder.CodeGen;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaBuilder.ViewModels;

public enum ItemKind
{
	Text,
	Number,
	Bool,
	Enum,
	Brush,
	BindingOnly,
	Event,
	Raw,
}

public partial class PropertyItemViewModel : ObservableObject
{
	private readonly Func<string?> _read;
	private readonly Action<string?, bool> _write;
	private readonly Func<object?>? _readLive;
	private readonly Func<string, string?>? _validate;
	private bool _syncing;
	private bool _bindingRequested;
	private string? _lastRaw;

	public PropertyItemViewModel(string name, ItemKind kind, PropertyMeta? meta, Func<string?> read, Action<string?, bool> write,
		Func<object?>? readLive = null, string? typeHint = null, Func<string, string?>? validate = null)
	{
		_validate = validate;
		Name = name;
		Kind = kind;
		Meta = meta;
		_read = read;
		_write = write;
		_readLive = readLive;
		Options = meta is not null && kind == ItemKind.Enum ? PropertyClassifier.GetOptions(meta.ValueType) : null;

		var valueType = meta is null ? typeof(string) : Nullable.GetUnderlyingType(meta.ValueType) ?? meta.ValueType;
		LiveCommit = kind == ItemKind.Text && (valueType == typeof(string) || valueType == typeof(object));
		TypeHint = typeHint ?? (meta is null ? name : $"{name}  ({ValueConverter.FriendlyName(meta.ValueType)})");
		Sync();
	}

	public string Name { get; }

	public ItemKind Kind { get; }

	public PropertyMeta? Meta { get; }

	public string TypeHint { get; }

	public IReadOnlyList<string>? Options { get; }

	public bool LiveCommit { get; }

	[ObservableProperty]
	public partial string Text { get; set; } = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(NameWeight))]
	public partial bool IsSet { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ShowTextEditor), nameof(ShowBoolEditor), nameof(ShowEnumEditor), nameof(ShowSwatch))]
	public partial bool IsBinding { get; set; }

	[ObservableProperty]
	public partial string Placeholder { get; set; } = "";

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	public partial string? Error { get; set; }

	[ObservableProperty]
	public partial bool IsVisible { get; set; } = true;

	[ObservableProperty]
	public partial bool IsChecked { get; set; }

	[ObservableProperty]
	public partial string? SelectedOption { get; set; }

	[ObservableProperty]
	public partial Color PickerColor { get; set; }

	[ObservableProperty]
	public partial IBrush? Swatch { get; set; }

	public bool HasError => Error is not null;

	public FontWeight NameWeight => IsSet ? FontWeight.SemiBold : FontWeight.Normal;

	public bool ShowTextEditor => !IsBinding && Kind is ItemKind.Text or ItemKind.Number or ItemKind.Brush or ItemKind.Event or ItemKind.Raw;

	public bool ShowBoolEditor => !IsBinding && Kind == ItemKind.Bool;

	public bool ShowEnumEditor => !IsBinding && Kind == ItemKind.Enum;

	public bool ShowSwatch => !IsBinding && Kind == ItemKind.Brush;

	public bool CanBind => Kind is not (ItemKind.Event or ItemKind.Raw or ItemKind.BindingOnly) && Meta is not null;

	public string BindingHint => Meta is not null && typeof(System.Windows.Input.ICommand).IsAssignableFrom(Meta.ValueType)
		? "command, for example SaveCommand"
		: "property of the view model";

	public void Sync()
	{
		string? raw = _read();
		if (Error is not null && raw == _lastRaw)
			return;
		_lastRaw = raw;

		_syncing = true;
		try
		{
			Error = null;
			IsSet = raw is not null;
			var markup = XamlMarkup.Parse(raw);
			bool bound = markup is { Name: "Binding" };
			if (raw is not null)
				_bindingRequested = false;

			if (bound)
			{
				IsBinding = true;
				string inner = raw!.Trim()[1..^1].Trim();
				Text = inner.Length > "Binding".Length ? inner["Binding".Length..].Trim() : "";
			}
			else if (raw is null && (Kind == ItemKind.BindingOnly || _bindingRequested))
			{
				IsBinding = true;
				Text = "";
			}
			else
			{
				IsBinding = false;
				Text = raw ?? "";
			}

			object? live = null;
			try
			{
				live = _readLive?.Invoke();
			}
			catch (Exception)
			{
			}
			string liveText = ValueFormatter.Format(live);
			Placeholder = IsBinding ? BindingHint : liveText;

			if (Kind == ItemKind.Bool)
				IsChecked = raw is not null && bool.TryParse(raw, out bool flag) ? flag : live is true;
			if (Kind == ItemKind.Enum)
				SelectedOption = raw is not null && markup is null ? MatchOption(raw) : MatchOption(liveText);
			if (Kind == ItemKind.Brush)
				UpdateSwatch(raw is not null && markup is null ? raw : liveText);
		}
		finally
		{
			_syncing = false;
		}
	}

	private string? MatchOption(string text) =>
		Options?.FirstOrDefault(o => string.Equals(o, text, StringComparison.OrdinalIgnoreCase));

	private void UpdateSwatch(string text)
	{
		IBrush? brush = null;
		try
		{
			if (text.Length > 0)
				brush = Brush.Parse(text);
		}
		catch (Exception)
		{
		}
		Swatch = brush;
		if (brush is ISolidColorBrush solid)
			PickerColor = solid.Color;
	}

	partial void OnTextChanged(string value)
	{
		if (_syncing)
			return;
		CommitText(value, typing: LiveCommit);
	}

	partial void OnIsCheckedChanged(bool value)
	{
		if (!_syncing)
			Apply(value ? "True" : "False", typing: false);
	}

	partial void OnSelectedOptionChanged(string? value)
	{
		if (!_syncing && value is not null)
			Apply(value, typing: false);
	}

	partial void OnPickerColorChanged(Color value)
	{
		if (_syncing)
			return;
		Apply(ValueFormatter.FormatColor(value), typing: true);
	}

	private void CommitText(string text, bool typing)
	{
		if (IsBinding)
		{
			string path = text.Trim();
			if (path.Length == 0)
			{
				Error = null;
				if (IsSet)
					Apply(null, typing);
				return;
			}
			string binding = XamlMarkup.MakeBinding(path);
			if (XamlMarkup.Parse(binding) is null)
			{
				Error = "The curly braces do not match.";
				return;
			}
			Error = null;
			Apply(binding, typing);
			return;
		}

		if (text.Length == 0)
		{
			Error = null;
			Apply(null, typing);
			return;
		}

		string value = text;
		if (Kind == ItemKind.Number && text.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
			value = "NaN";

		string? error = Validate(value);
		Error = error;
		if (error is not null)
			return;
		Apply(value, typing);
		if (value != text)
			Dispatcher.UIThread.Post(Sync);
	}

	private string? Validate(string value)
	{
		if (_validate is not null)
			return _validate(value);
		if (XamlMarkup.IsMarkup(value))
			return XamlMarkup.Parse(value) is null ? "The curly braces do not match." : null;
		if (Kind == ItemKind.Event)
			return CodeNames.IsIdentifier(value) ? null : "Give the name of a method, for example OnSaveClick.";
		if (Meta is null || Kind == ItemKind.Raw)
			return null;
		var type = Nullable.GetUnderlyingType(Meta.ValueType) ?? Meta.ValueType;
		if (type == typeof(IImage) || type == typeof(Bitmap) || !ValueConverter.CanConvert(type))
			return null;
		return ValueConverter.TryConvert(XamlMarkup.Unescape(value), Meta.ValueType, null, out _, out string? error) ? null : error;
	}

	private void Apply(string? raw, bool typing)
	{
		_lastRaw = raw;
		_write(raw, typing);
	}

	[RelayCommand]
	private void Reset()
	{
		_bindingRequested = false;
		Error = null;
		if (IsSet)
			Apply(null, typing: false);
		else
			Sync();
	}

	[RelayCommand]
	private void ToggleBinding()
	{
		if (!CanBind)
			return;
		Error = null;
		if (IsBinding)
		{
			_bindingRequested = false;
			if (IsSet)
				Apply(null, typing: false);
			else
				Sync();
		}
		else
		{
			_bindingRequested = true;
			_syncing = true;
			IsBinding = true;
			Text = "";
			Placeholder = BindingHint;
			_syncing = false;
		}
	}

	[RelayCommand]
	private void PickNamedColor(string name)
	{
		Error = null;
		Apply(name, typing: false);
	}
}
