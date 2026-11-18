using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Xaml;

public sealed class XamlReadException : Exception
{
	public XamlReadException(string message, int line, int column) : base(message)
	{
		Line = line;
		Column = column;
	}

	public int Line { get; }

	public int Column { get; }
}

public static partial class DesignXamlReader
{
	private const string TextNodeName = "#text";

	public static DesignDocument Parse(string xaml)
	{
		var namespaces = new List<XamlNamespace>();
		var roots = ParseNodes(xaml, namespaces, context: null);
		if (roots.Count != 1)
			throw new XamlReadException("The XAML must have exactly one root element.", 1, 1);
		return new DesignDocument(roots[0], namespaces);
	}

	public static List<DesignNode> ParseFragment(string xaml, DesignDocument? context, out List<XamlNamespace> declared)
	{
		declared = new List<XamlNamespace>();
		return ParseNodes(xaml, declared, context ?? new DesignDocument(new DesignNode("UserControl")));
	}

	private static List<DesignNode> ParseNodes(string xaml, List<XamlNamespace> namespaces, DesignDocument? context)
	{
		var known = new Dictionary<string, string>(StringComparer.Ordinal);
		if (context is not null)
		{
			foreach (var ns in context.Namespaces)
				known[ns.Prefix] = ns.Uri;
			known.TryAdd("", XamlNs.Avalonia);
			known.TryAdd("x", XamlNs.Xaml);
			known.TryAdd("d", XamlNs.Design);
			known.TryAdd("mc", XamlNs.Compatibility);
		}

		var guessed = new List<XamlNamespace>();
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				namespaces.Clear();
				var nodes = ParseCore(xaml, namespaces, context is null ? null : known);
				foreach (var ns in guessed)
					AddNamespace(namespaces, ns.Prefix, ns.Uri);
				return nodes;
			}
			catch (XmlException ex)
			{
				var match = UndeclaredPrefixRegex().Match(ex.Message);
				if (context is not null && attempt < 20 && match.Success && known.TryAdd(match.Groups[1].Value, "using:" + match.Groups[1].Value))
				{
					guessed.Add(new XamlNamespace(match.Groups[1].Value, known[match.Groups[1].Value]));
					continue;
				}
				throw new XamlReadException(CleanMessage(ex.Message), ex.LineNumber, ex.LinePosition);
			}
		}
	}

	private static List<DesignNode> ParseCore(string xaml, List<XamlNamespace> namespaces, Dictionary<string, string>? known)
	{
		var settings = new XmlReaderSettings
		{
			ConformanceLevel = known is null ? ConformanceLevel.Document : ConformanceLevel.Fragment,
			IgnoreWhitespace = false,
			IgnoreComments = false,
			IgnoreProcessingInstructions = true,
			DtdProcessing = DtdProcessing.Prohibit,
		};

		XmlParserContext? parserContext = null;
		if (known is not null)
		{
			var manager = new XmlNamespaceManager(new NameTable());
			foreach (var (prefix, uri) in known)
				manager.AddNamespace(prefix, uri);
			parserContext = new XmlParserContext(null, manager, null, XmlSpace.None);
		}

		using var reader = XmlReader.Create(new StringReader(xaml), settings, parserContext);
		var lineInfo = (IXmlLineInfo)reader;

		var roots = new List<DesignNode>();
		var stack = new Stack<DesignNode>();
		var pendingComments = new List<string>();
		bool pendingBlankLine = false;

		while (reader.Read())
		{
			switch (reader.NodeType)
			{
				case XmlNodeType.Element:
					{
						var node = new DesignNode(reader.LocalName, reader.Prefix)
						{
							SourceLine = lineInfo.LineNumber,
							SourceEndLine = lineInfo.LineNumber,
							BlankLineBefore = pendingBlankLine && (stack.Count == 0 ? roots.Count > 0 : stack.Peek().Children.Count > 0),
						};
						node.Comments.AddRange(pendingComments);
						pendingComments.Clear();
						pendingBlankLine = false;

						bool isEmpty = reader.IsEmptyElement;
						if (reader.MoveToFirstAttribute())
						{
							do
							{
								node.SourceEndLine = Math.Max(node.SourceEndLine, lineInfo.LineNumber);
								if (reader.Prefix == "xmlns")
									AddNamespace(namespaces, reader.LocalName, reader.Value);
								else if (reader.Name == "xmlns")
									AddNamespace(namespaces, "", reader.Value);
								else
									node.AddAttributeRaw(reader.Name, reader.Value);
							} while (reader.MoveToNextAttribute());
							reader.MoveToElement();
						}

						if (stack.Count > 0)
							stack.Peek().Add(node);
						else
							roots.Add(node);

						if (!isEmpty)
							stack.Push(node);
						break;
					}

				case XmlNodeType.EndElement:
					{
						var node = stack.Pop();
						node.SourceEndLine = lineInfo.LineNumber;
						node.TrailingComments.AddRange(pendingComments);
						pendingComments.Clear();
						pendingBlankLine = false;
						FinishElement(node);
						break;
					}

				case XmlNodeType.Text:
				case XmlNodeType.CDATA:
				case XmlNodeType.SignificantWhitespace:
					if (stack.Count > 0)
						stack.Peek().Add(new DesignNode(TextNodeName) { Text = reader.Value });
					break;

				case XmlNodeType.Whitespace:
					if (reader.Value.Count(c => c == '\n') >= 2)
						pendingBlankLine = true;
					break;

				case XmlNodeType.Comment:
					pendingComments.Add(reader.Value);
					break;
			}
		}

		return roots;
	}

	private static void AddNamespace(List<XamlNamespace> namespaces, string prefix, string uri)
	{
		if (!namespaces.Any(ns => ns.Prefix == prefix))
			namespaces.Add(new XamlNamespace(prefix, uri));
	}

	private static void FinishElement(DesignNode node)
	{
		var texts = node.Children.Where(c => c.TypeName == TextNodeName).ToList();
		if (texts.Count == 0)
			return;

		bool hasElements = node.Children.Count > texts.Count;
		if (hasElements && IsInlineHost(node.TypeName))
		{
			var first = node.Children[0];
			var last = node.Children[^1];
			foreach (var text in texts)
			{
				string value = CollapseWhitespace(text.Text ?? "");
				if (text == first)
					value = value.TrimStart();
				if (text == last)
					value = value.TrimEnd();
				if (value.Length == 0)
				{
					node.RemoveChild(text);
					continue;
				}
				text.TypeName = "Run";
				text.Text = null;
				text.Set("Text", value);
			}
			return;
		}

		string all = CollapseWhitespace(string.Concat(texts.Select(t => t.Text))).Trim();
		foreach (var text in texts)
			node.RemoveChild(text);
		node.Text = all.Length == 0 ? null : all;
	}

	private static bool IsInlineHost(string typeName) =>
		typeName is "TextBlock" or "SelectableTextBlock" or "Span" or "Bold" or "Italic" or "Underline" or "InlineUIContainer";

	private static string CollapseWhitespace(string text)
	{
		var builder = new StringBuilder(text.Length);
		bool lastWasSpace = false;
		foreach (char c in text)
		{
			if (char.IsWhiteSpace(c))
			{
				if (!lastWasSpace)
					builder.Append(' ');
				lastWasSpace = true;
			}
			else
			{
				builder.Append(c);
				lastWasSpace = false;
			}
		}
		return builder.ToString();
	}

	private static string CleanMessage(string message)
	{
		int index = message.LastIndexOf(" Line ", StringComparison.Ordinal);
		return index > 0 ? message[..index] : message;
	}

	[GeneratedRegex(@"'([\w\.\-]+)' is an undeclared prefix")]
	private static partial Regex UndeclaredPrefixRegex();
}
