using System.Text;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Xaml;

public sealed class XamlWriterOptions
{
	public int IndentSize { get; init; } = 4;

	public int MaxLineLength { get; init; } = 110;

	public static XamlWriterOptions Default { get; } = new();
}

public static class DesignXamlWriter
{
	public static string Write(DesignDocument document, XamlWriterOptions? options = null)
	{
		var writer = new Writer(options ?? XamlWriterOptions.Default, trackLines: true);
		writer.WriteElement(document.Root, 0, document.Namespaces, isRoot: true, isFirst: true);
		return writer.ToString();
	}

	public static string WriteFragment(DesignDocument document, IEnumerable<DesignNode> nodes, bool withNamespaces,
		XamlWriterOptions? options = null)
	{
		var writer = new Writer(options ?? XamlWriterOptions.Default, trackLines: false);
		bool first = true;
		foreach (var node in nodes)
		{
			var namespaces = withNamespaces ? UsedNamespaces(document, node) : null;
			writer.WriteElement(node, 0, namespaces, isRoot: false, isFirst: first);
			first = false;
		}
		return writer.ToString();
	}

	private static List<XamlNamespace> UsedNamespaces(DesignDocument document, DesignNode node)
	{
		var prefixes = new HashSet<string>(StringComparer.Ordinal) { "" };
		foreach (var n in node.SelfAndDescendants())
		{
			prefixes.Add(n.Prefix);
			foreach (var attribute in n.Attributes)
			{
				int colon = attribute.Name.IndexOf(':');
				if (colon > 0)
					prefixes.Add(attribute.Name[..colon]);
				foreach (var ns in document.Namespaces)
					if (ns.Prefix.Length > 0 && attribute.Value.Contains(ns.Prefix + ":", StringComparison.Ordinal))
						prefixes.Add(ns.Prefix);
			}
		}
		return document.Namespaces.Where(ns => prefixes.Contains(ns.Prefix)).ToList();
	}

	public static string EscapeAttribute(string value)
	{
		if (value.AsSpan().IndexOfAny("&<\"\n\r\t") < 0)
			return value;
		var builder = new StringBuilder(value.Length + 8);
		foreach (char c in value)
		{
			switch (c)
			{
				case '&': builder.Append("&amp;"); break;
				case '<': builder.Append("&lt;"); break;
				case '"': builder.Append("&quot;"); break;
				case '\n': builder.Append("&#10;"); break;
				case '\r': builder.Append("&#13;"); break;
				case '\t': builder.Append("&#9;"); break;
				default: builder.Append(c); break;
			}
		}
		return builder.ToString();
	}

	public static string EscapeText(string value) =>
		value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

	private sealed class Writer
	{
		private readonly StringBuilder _text = new();
		private readonly XamlWriterOptions _options;
		private readonly bool _trackLines;
		private int _line = 1;

		public Writer(XamlWriterOptions options, bool trackLines)
		{
			_options = options;
			_trackLines = trackLines;
		}

		public override string ToString() => _text.ToString();

		private void NewLine()
		{
			_text.Append('\n');
			_line++;
		}

		private void Append(string text)
		{
			_text.Append(text);
			foreach (char c in text)
				if (c == '\n')
					_line++;
		}

		public void WriteElement(DesignNode node, int level, IReadOnlyList<XamlNamespace>? namespaces, bool isRoot, bool isFirst)
		{
			string pad = new(' ', level * _options.IndentSize);
			string childPad = new(' ', (level + 1) * _options.IndentSize);

			if (node.BlankLineBefore && !isFirst)
				NewLine();

			foreach (string comment in node.Comments)
			{
				Append(pad + "<!--" + comment + "-->");
				NewLine();
			}

			if (_trackLines)
				node.SourceLine = _line;

			var attributes = new List<string>();
			if (namespaces is not null)
			{
				foreach (var ns in namespaces)
					attributes.Add((ns.Prefix.Length == 0 ? "xmlns" : "xmlns:" + ns.Prefix) + "=\"" + EscapeAttribute(ns.Uri) + "\"");
			}
			foreach (var attribute in node.Attributes)
				attributes.Add(attribute.Name + "=\"" + EscapeAttribute(attribute.Value) + "\"");

			bool hasText = !string.IsNullOrEmpty(node.Text);
			bool hasContent = node.Children.Count > 0 || hasText || node.TrailingComments.Count > 0;
			string open = pad + "<" + node.ElementName;
			string close = hasContent ? ">" : " />";

			int oneLineLength = open.Length + attributes.Sum(a => a.Length + 1) + close.Length;
			bool multiLine = attributes.Count > 1 && (oneLineLength > _options.MaxLineLength || (isRoot && attributes.Count > 2));
			if (!multiLine)
			{
				Append(open);
				foreach (string attribute in attributes)
					Append(" " + attribute);
			}
			else
			{
				string attributePad = new(' ', open.Length + 1);
				Append(open + " " + attributes[0]);
				for (int i = 1; i < attributes.Count; i++)
				{
					NewLine();
					Append(attributePad + attributes[i]);
				}
			}
			Append(close);

			if (!hasContent)
			{
				if (_trackLines)
					node.SourceEndLine = _line;
				NewLine();
				return;
			}

			string endTag = "</" + node.ElementName + ">";
			if (node.Children.Count == 0 && node.TrailingComments.Count == 0)
			{
				string text = EscapeText(node.Text!);
				if (!multiLine && !text.Contains('\n') && oneLineLength + text.Length + endTag.Length <= _options.MaxLineLength)
				{
					Append(text + endTag);
				}
				else
				{
					NewLine();
					WriteTextLines(text, childPad);
					Append(pad + endTag);
				}
			}
			else
			{
				NewLine();
				if (hasText)
					WriteTextLines(EscapeText(node.Text!), childPad);
				bool first = !hasText;
				foreach (var child in node.Children)
				{
					WriteElement(child, level + 1, null, isRoot: false, isFirst: first);
					first = false;
				}
				foreach (string comment in node.TrailingComments)
				{
					Append(childPad + "<!--" + comment + "-->");
					NewLine();
				}
				Append(pad + endTag);
			}

			if (_trackLines)
				node.SourceEndLine = _line;
			NewLine();
		}

		private void WriteTextLines(string text, string pad)
		{
			foreach (string line in text.Split('\n'))
			{
				Append(pad + line.Trim());
				NewLine();
			}
		}
	}
}
