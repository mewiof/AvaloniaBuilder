using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AvaloniaBuilder.Metadata;

public sealed class ValueContext
{
	public string? DocumentDirectory { get; init; }

	public string? ProjectDirectory { get; init; }
}

public static class ValueConverter
{
	private delegate object? Converter(string text, ValueContext? context);

	private static readonly ConcurrentDictionary<Type, Converter?> Converters = new();
	private static readonly ConcurrentDictionary<string, Bitmap> Bitmaps = new();

	public static bool CanConvert(Type type) => GetConverter(type) is not null;

	public static bool TryConvert(string text, Type targetType, ValueContext? context, out object? value, out string? error)
	{
		value = null;
		var converter = GetConverter(targetType);
		if (converter is null)
		{
			error = $"A value of type {FriendlyName(targetType)} cannot be written as text.";
			return false;
		}

		try
		{
			value = converter(text, context);
			error = null;
			return true;
		}
		catch (Exception ex)
		{
			if (ex is TargetInvocationException { InnerException: { } inner })
				ex = inner;
			error = $"\"{text}\" is not a valid {FriendlyName(targetType)}. {ex.Message}";
			return false;
		}
	}

	private static Converter? GetConverter(Type type) => Converters.GetOrAdd(type, Create);

	private static Converter? Create(Type declaredType)
	{
		var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

		if (type == typeof(string) || type == typeof(object))
			return (text, _) => text;
		if (type == typeof(bool))
			return (text, _) => bool.Parse(text);
		if (type.IsEnum)
			return (text, _) => ParseEnum(type, text);
		if (type == typeof(double))
			return (text, _) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
		if (type == typeof(float))
			return (text, _) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
		if (type == typeof(decimal))
			return (text, _) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
		if (type == typeof(int))
			return (text, _) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (type == typeof(long))
			return (text, _) => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (type == typeof(short))
			return (text, _) => short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (type == typeof(byte))
			return (text, _) => byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (type == typeof(uint))
			return (text, _) => uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (type == typeof(char))
			return (text, _) => text.Length == 1 ? text[0] : throw new FormatException("Give exactly one character.");

		if (type == typeof(Type) || typeof(ICommand).IsAssignableFrom(type) || typeof(Delegate).IsAssignableFrom(type))
			return null;

		if (type.IsInterface && type.IsAssignableFrom(typeof(Avalonia.Media.Immutable.ImmutableSolidColorBrush)))
			return (text, _) => Brush.Parse(text);
		if (type == typeof(IImage) || type == typeof(Bitmap))
			return LoadBitmap;
		if (type == typeof(Uri))
			return (text, _) => new Uri(text, UriKind.RelativeOrAbsolute);
		if (type == typeof(CultureInfo))
			return (text, _) => new CultureInfo(text);
		if (type.IsAssignableFrom(typeof(AvaloniaList<Point>)) && type != typeof(System.Collections.IEnumerable) && type != typeof(System.Collections.IList))
			return (text, _) => ParsePoints(text);
		if (type == typeof(AvaloniaList<double>))
			return (text, _) => new AvaloniaList<double>(SplitNumbers(text));

		if (type.GetCustomAttribute<TypeConverterAttribute>() is { } attribute
			&& Type.GetType(attribute.ConverterTypeName) is { } converterType
			&& Activator.CreateInstance(converterType) is TypeConverter typeConverter
			&& typeConverter.CanConvertFrom(typeof(string)))
		{
			return (text, _) => typeConverter.ConvertFrom(null, CultureInfo.InvariantCulture, text);
		}

		const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
		var parseWithCulture = type.GetMethod("Parse", Static, new[] { typeof(string), typeof(IFormatProvider) });
		if (parseWithCulture is not null && type.IsAssignableFrom(parseWithCulture.ReturnType))
			return (text, _) => parseWithCulture.Invoke(null, new object[] { text, CultureInfo.InvariantCulture });
		var parse = type.GetMethod("Parse", Static, new[] { typeof(string) });
		if (parse is not null && type.IsAssignableFrom(parse.ReturnType))
			return (text, _) => parse.Invoke(null, new object[] { text });

		var constructor = type.GetConstructor(new[] { typeof(string) });
		if (constructor is not null)
			return (text, _) => constructor.Invoke(new object[] { text });

		return null;
	}

	private static object ParseEnum(Type type, string text)
	{
		object value = Enum.Parse(type, text.Trim(), ignoreCase: true);
		return value;
	}

	private static IEnumerable<double> SplitNumbers(string text) =>
		text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(part => double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture));

	private static AvaloniaList<Point> ParsePoints(string text)
	{
		var numbers = SplitNumbers(text).ToList();
		if (numbers.Count % 2 != 0)
			throw new FormatException("A point needs two numbers: x,y.");
		var points = new AvaloniaList<Point>();
		for (int i = 0; i < numbers.Count; i += 2)
			points.Add(new Point(numbers[i], numbers[i + 1]));
		return points;
	}

	private static object LoadBitmap(string text, ValueContext? context)
	{
		string? path = ResolveFile(text, context)
			?? throw new FileNotFoundException($"The image file was not found: {text}");
		string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
		return Bitmaps.GetOrAdd(key, _ => new Bitmap(path));
	}

	public static string? ResolveFile(string text, ValueContext? context)
	{
		var candidates = new List<string>();
		if (text.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
		{
			if (context?.ProjectDirectory is { } project && Uri.TryCreate(text, UriKind.Absolute, out var uri))
				candidates.Add(Path.Combine(project, Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/')));
		}
		else if (text.StartsWith('/'))
		{
			if (context?.ProjectDirectory is { } project)
				candidates.Add(Path.Combine(project, text.TrimStart('/')));
		}
		else if (Path.IsPathRooted(text))
		{
			candidates.Add(text);
		}
		else
		{
			if (context?.DocumentDirectory is { } document)
				candidates.Add(Path.Combine(document, text));
			if (context?.ProjectDirectory is { } project)
				candidates.Add(Path.Combine(project, text));
		}

		foreach (string candidate in candidates)
		{
			try
			{
				if (File.Exists(candidate))
					return Path.GetFullPath(candidate);
			}
			catch (Exception)
			{
			}
		}
		return null;
	}

	public static string FriendlyName(Type type)
	{
		if (Nullable.GetUnderlyingType(type) is { } inner)
			return FriendlyName(inner);
		if (type == typeof(bool)) return "bool";
		if (type == typeof(int)) return "int";
		if (type == typeof(double)) return "number";
		if (type == typeof(decimal)) return "number";
		if (type == typeof(string)) return "text";
		if (type == typeof(object)) return "object";
		if (type == typeof(IBrush)) return "brush";
		if (type.IsGenericType)
			return type.Name[..type.Name.IndexOf('`')] + "<" + string.Join(", ", type.GetGenericArguments().Select(FriendlyName)) + ">";
		return type.Name;
	}
}
