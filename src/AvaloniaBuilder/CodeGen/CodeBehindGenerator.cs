using System.Text;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.CodeGen;

public static class CodeBehindGenerator
{
	private sealed record Handler(string Method, Type ArgsType);

	public static string Generate(DesignDocument document)
	{
		var (ns, className) = CodeNames.Split(document.ViewClass ?? ViewModelGenerator.DefaultViewClass);
		string baseClass = document.Root.TypeName;

		var handlers = FindHandlers(document);
		var usings = new SortedSet<string>(StringComparer.Ordinal) { "Avalonia.Controls" };
		foreach (var handler in handlers)
			if (handler.ArgsType.Namespace is { } argsNamespace)
				usings.Add(argsNamespace);
		if (XamlTypes.Resolve(document, document.Root) is { Namespace: { } baseNamespace })
			usings.Add(baseNamespace);

		var code = new StringBuilder();
		foreach (string u in CodeNames.SortUsings(usings))
			code.Append("using ").Append(u).Append(";\n");
		code.Append('\n');
		if (ns.Length > 0)
			code.Append("namespace ").Append(ns).Append(";\n\n");

		code.Append("public partial class ").Append(className).Append(" : ").Append(baseClass).Append("\n{\n");
		code.Append("    public ").Append(className).Append("()\n    {\n");
		code.Append("        InitializeComponent();\n    }\n");

		foreach (var handler in handlers)
		{
			code.Append('\n');
			code.Append("    private void ").Append(handler.Method).Append("(object? sender, ").Append(handler.ArgsType.Name).Append(" e)\n");
			code.Append("    {\n    }\n");
		}

		code.Append("}\n");
		return code.ToString();
	}

	private static List<Handler> FindHandlers(DesignDocument document)
	{
		var handlers = new List<Handler>();
		foreach (var node in document.Root.SelfAndDescendants())
		{
			var type = XamlTypes.Resolve(document, node);
			if (type is null)
				continue;
			var meta = XamlTypes.GetMeta(type);
			foreach (var attribute in node.Attributes)
			{
				if (meta.FindEvent(attribute.Name) is not { } info || XamlMarkup.IsMarkup(attribute.Value))
					continue;
				if (!CodeNames.IsIdentifier(attribute.Value) || handlers.Any(h => h.Method == attribute.Value))
					continue;
				handlers.Add(new Handler(attribute.Value, info.ArgsType));
			}
		}
		return handlers;
	}
}
