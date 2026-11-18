using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvaloniaBuilder.Metadata;

public enum EditorKind
{
	Text,
	Number,
	Bool,
	Enum,
	Brush,
	Command,
	BindingOnly,
	Unsupported,
}

public static class PropertyClassifier
{
	public const string Common = "Common";
	public const string Layout = "Layout";
	public const string Appearance = "Appearance";
	public const string TextGroup = "Text";
	public const string Behavior = "Behavior";
	public const string Other = "Other";

	private static readonly string[] LayoutOrder =
	{
		"Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight", "Margin", "Padding",
		"HorizontalAlignment", "VerticalAlignment", "HorizontalContentAlignment", "VerticalContentAlignment",
		"ZIndex", "ClipToBounds", "UseLayoutRounding",
	};

	private static readonly string[] AppearanceOrder =
	{
		"Classes", "Background", "Foreground", "BorderBrush", "BorderThickness", "CornerRadius", "BoxShadow",
		"Fill", "Stroke", "StrokeThickness", "StrokeDashArray", "StrokeLineCap", "StrokeJoin", "Opacity", "IsVisible",
		"OpacityMask", "BackgroundSizing", "Effect", "RenderTransform", "RenderTransformOrigin", "FlowDirection",
	};

	private static readonly string[] TextOrder =
	{
		"FontFamily", "FontSize", "FontWeight", "FontStyle", "FontStretch", "TextAlignment", "TextWrapping",
		"TextTrimming", "TextDecorations", "LineHeight", "LineSpacing", "LetterSpacing", "MaxLines", "FontFeatures",
		"BaselineOffset",
	};

	private static readonly string[] BehaviorOrder =
	{
		"IsEnabled", "Focusable", "IsHitTestVisible", "IsTabStop", "TabIndex", "Cursor", "Tag", "DataContext",
	};

	private static readonly string[] CommonOrder =
	{
		"Title", "Header", "Content", "Text", "PlaceholderText", "IsChecked", "Value", "Minimum", "Maximum",
		"Command", "CommandParameter", "ItemsSource", "SelectedIndex", "SelectedItem", "Orientation", "Spacing",
		"RowDefinitions", "ColumnDefinitions", "RowSpacing", "ColumnSpacing", "Rows", "Columns", "LastChildFill",
		"Source", "Stretch", "Data", "IsExpanded", "IsPaneOpen", "DisplayMode", "IsReadOnly", "AcceptsReturn",
		"PasswordChar", "IsIndeterminate", "ShowProgressText", "TickFrequency", "IsSnapToTickEnabled", "Increment",
		"FormatString", "GroupName", "IsThreeState", "OnContent", "OffContent", "SelectedDate", "SelectedTime",
		"HorizontalScrollBarVisibility", "VerticalScrollBarVisibility", "TabStripPlacement", "SelectionMode",
		"StartPoint", "EndPoint", "Points", "RadiusX", "RadiusY", "StartAngle", "SweepAngle",
		"Width", "Height", "SizeToContent", "CanResize", "WindowStartupLocation",
	};

	private static readonly HashSet<Type> BaseTypes = new()
	{
		typeof(AvaloniaObject), typeof(Animatable), typeof(StyledElement), typeof(Visual), typeof(Layoutable),
		typeof(Interactive), typeof(InputElement), typeof(Control), typeof(TemplatedControl), typeof(TopLevel),
		typeof(WindowBase),
	};

	private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
	{
		"Name", "Parent", "TemplatedParent", "Theme", "Resources", "Styles", "Transitions", "Clock",
		"ActualThemeVariant", "RequestedThemeVariant", "Clip", "CacheMode", "FocusAdorner", "IsTemplateFocusTarget",
	};

	public static bool IsHidden(string name) => Hidden.Contains(name);

	public static EditorKind GetKind(Type declaredType)
	{
		var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
		if (type == typeof(bool))
			return EditorKind.Bool;
		if (type.IsEnum)
			return EditorKind.Enum;
		if (type == typeof(double) || type == typeof(float) || type == typeof(decimal) || type == typeof(int)
			|| type == typeof(long) || type == typeof(short) || type == typeof(byte) || type == typeof(uint))
			return EditorKind.Number;
		if (typeof(ICommand).IsAssignableFrom(type))
			return EditorKind.Command;
		if (typeof(IBrush).IsAssignableFrom(type) || type == typeof(Color))
			return EditorKind.Brush;
		if (type == typeof(string) || type == typeof(object))
			return EditorKind.Text;
		if (typeof(BindingBase).IsAssignableFrom(type))
			return EditorKind.BindingOnly;
		if (type.IsInterface && typeof(IEnumerable).IsAssignableFrom(type) && !ValueConverter.CanConvert(type))
			return EditorKind.BindingOnly;
		if (type.Name == "ISelectionModel")
			return EditorKind.BindingOnly;
		if (ValueConverter.CanConvert(type))
			return EditorKind.Text;
		return EditorKind.Unsupported;
	}

	public static IReadOnlyList<string>? GetOptions(Type declaredType)
	{
		var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
		if (type.IsEnum)
			return Enum.GetNames(type);
		return null;
	}

	public static string GetCategory(PropertyMeta property)
	{
		string name = property.Name;
		if (Array.IndexOf(LayoutOrder, name) >= 0)
			return Layout;
		if (Array.IndexOf(AppearanceOrder, name) >= 0)
			return Appearance;
		if (Array.IndexOf(TextOrder, name) >= 0)
			return TextGroup;
		if (Array.IndexOf(BehaviorOrder, name) >= 0)
			return Behavior;
		return BaseTypes.Contains(property.DeclaringType) ? Other : Common;
	}

	public static int GetRank(string category, string name)
	{
		string[] order = category switch
		{
			Layout => LayoutOrder,
			Appearance => AppearanceOrder,
			TextGroup => TextOrder,
			Behavior => BehaviorOrder,
			Common => CommonOrder,
			_ => Array.Empty<string>(),
		};
		int index = Array.IndexOf(order, name);
		return index >= 0 ? index : 1000;
	}
}
