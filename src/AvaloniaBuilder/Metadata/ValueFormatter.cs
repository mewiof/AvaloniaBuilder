using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace AvaloniaBuilder.Metadata;

public static class ValueFormatter
{
	public static string Format(object? value)
	{
		switch (value)
		{
			case null:
				return "";
			case string text:
				return text;
			case bool flag:
				return flag ? "True" : "False";
			case double number:
				return FormatNumber(number);
			case float number:
				return FormatNumber(number);
			case Enum:
				return value.ToString()!;
			case ISolidColorBrush brush:
				return FormatColor(brush.Color, brush.Opacity);
			case Color color:
				return FormatColor(color, 1);
			case Thickness t:
				if (t.Left == t.Top && t.Left == t.Right && t.Left == t.Bottom)
					return FormatNumber(t.Left);
				if (t.Left == t.Right && t.Top == t.Bottom)
					return FormatNumber(t.Left) + "," + FormatNumber(t.Top);
				return string.Join(",", FormatNumber(t.Left), FormatNumber(t.Top), FormatNumber(t.Right), FormatNumber(t.Bottom));
			case CornerRadius r:
				if (r.TopLeft == r.TopRight && r.TopLeft == r.BottomRight && r.TopLeft == r.BottomLeft)
					return FormatNumber(r.TopLeft);
				return string.Join(",", FormatNumber(r.TopLeft), FormatNumber(r.TopRight), FormatNumber(r.BottomRight), FormatNumber(r.BottomLeft));
			case FontFamily family:
				return family.Name;
			case IFormattable formattable:
				return formattable.ToString(null, CultureInfo.InvariantCulture);
		}

		string? result = value.ToString();
		if (result is null || result == value.GetType().FullName || result == value.GetType().ToString())
			return "";
		return result;
	}

	public static string FormatNumber(double number)
	{
		if (double.IsNaN(number))
			return "NaN";
		if (double.IsPositiveInfinity(number))
			return "Infinity";
		if (double.IsNegativeInfinity(number))
			return "-Infinity";
		return Math.Round(number, 2).ToString("0.##", CultureInfo.InvariantCulture);
	}

	public static string FormatColor(Color color, double opacity = 1)
	{
		byte alpha = (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1));
		return alpha == 255
			? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
			: $"#{alpha:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
	}
}
