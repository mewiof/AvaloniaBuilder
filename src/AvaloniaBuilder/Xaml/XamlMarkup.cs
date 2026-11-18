using System.Text;

namespace AvaloniaBuilder.Xaml;

public sealed class XamlMarkup
{
	private XamlMarkup(string name, List<string> positional, List<KeyValuePair<string, string>> named)
	{
		Name = name;
		Positional = positional;
		Named = named;
	}

	public string Name { get; }

	public IReadOnlyList<string> Positional { get; }

	public IReadOnlyList<KeyValuePair<string, string>> Named { get; }

	public bool IsBinding => Name is "Binding" or "CompiledBinding" or "ReflectionBinding";

	public bool IsResource => Name is "StaticResource" or "DynamicResource";

	public string? BindingPath => IsBinding ? (Positional.Count > 0 ? Positional[0] : Get("Path")) ?? "" : null;

	public string? ResourceKey => IsResource ? (Positional.Count > 0 ? Positional[0] : Get("ResourceKey")) : null;

	public string? Get(string name)
	{
		foreach (var pair in Named)
			if (pair.Key == name)
				return pair.Value;
		return null;
	}

	public static bool IsMarkup(string? value) =>
		value is { Length: > 1 } && value[0] == '{' && !value.StartsWith("{}", StringComparison.Ordinal);

	public static string Unescape(string value) =>
		value.StartsWith("{}", StringComparison.Ordinal) ? value[2..] : value;

	public static string MakeBinding(string pathAndOptions) => "{Binding " + pathAndOptions.Trim() + "}";

	public static XamlMarkup? Parse(string? value)
	{
		if (!IsMarkup(value))
			return null;
		string text = value!.Trim();
		if (text[^1] != '}' || !BracesMatch(text))
			return null;

		string inner = text[1..^1].Trim();
		int space = 0;
		while (space < inner.Length && !char.IsWhiteSpace(inner[space]) && inner[space] != ',')
			space++;
		string name = inner[..space];
		if (name.Length == 0)
			return null;

		var positional = new List<string>();
		var named = new List<KeyValuePair<string, string>>();
		foreach (string part in SplitTopLevel(inner[space..], ','))
		{
			string item = part.Trim();
			if (item.Length == 0)
				continue;
			int equals = FindNameEquals(item);
			if (equals > 0)
				named.Add(new KeyValuePair<string, string>(item[..equals].Trim(), item[(equals + 1)..].Trim()));
			else
				positional.Add(item);
		}
		return new XamlMarkup(name, positional, named);
	}

	private static bool BracesMatch(string text)
	{
		int depth = 0;
		bool quoted = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\\')
			{
				i++;
				continue;
			}
			if (c == '\'')
				quoted = !quoted;
			if (quoted)
				continue;
			if (c == '{')
				depth++;
			else if (c == '}')
			{
				depth--;
				if (depth == 0 && i != text.Length - 1)
					return false;
				if (depth < 0)
					return false;
			}
		}
		return depth == 0 && !quoted;
	}

	private static List<string> SplitTopLevel(string text, char separator)
	{
		var parts = new List<string>();
		var current = new StringBuilder();
		int depth = 0;
		bool quoted = false;
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c == '\\' && i + 1 < text.Length)
			{
				current.Append(c).Append(text[++i]);
				continue;
			}
			if (c == '\'')
				quoted = !quoted;
			else if (!quoted && (c == '{' || c == '[' || c == '('))
				depth++;
			else if (!quoted && (c == '}' || c == ']' || c == ')'))
				depth--;

			if (c == separator && depth == 0 && !quoted)
			{
				parts.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(c);
			}
		}
		parts.Add(current.ToString());
		return parts;
	}

	private static int FindNameEquals(string item)
	{
		for (int i = 0; i < item.Length; i++)
		{
			char c = item[i];
			if (c == '=')
				return i;
			if (!(char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '.' || char.IsWhiteSpace(c)))
				return -1;
		}
		return -1;
	}
}
