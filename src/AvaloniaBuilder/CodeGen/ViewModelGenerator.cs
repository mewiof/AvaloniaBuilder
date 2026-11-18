using System.Collections;
using System.Text;
using System.Windows.Input;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;
using AvaloniaBuilder.Xaml;

namespace AvaloniaBuilder.CodeGen;

public sealed class CodeGenOptions
{
	public bool UsePartialProperties { get; init; } = true;

	public string ViewModelBaseClass { get; init; } = "ObservableObject";
}

public sealed class ViewModelProperty
{
	private readonly List<(string Type, int Weight)> _candidates = new();

	public ViewModelProperty(string name) => Name = name;

	public string Name { get; }

	public ViewModelClass? Nested { get; set; }

	public string? ItemType { get; set; }

	public bool IsCollection => ItemType is not null;

	public string Type
	{
		get
		{
			if (Nested is not null)
				return Nested.Name;
			if (ItemType is not null)
				return $"ObservableCollection<{ItemType}>";
			if (_candidates.Count == 0)
				return "object?";
			return _candidates.OrderByDescending(c => c.Weight).First().Type;
		}
	}

	public void AddHint(string type, int weight) => _candidates.Add((type, weight));
}

public sealed class ViewModelCommand
{
	public ViewModelCommand(string bindingName) => BindingName = bindingName;

	public string BindingName { get; }

	public bool UsesAttribute => BindingName.Length > "Command".Length && BindingName.EndsWith("Command", StringComparison.Ordinal);

	public string MethodName => UsesAttribute ? BindingName[..^"Command".Length] : BindingName + "Execute";

	public bool HasParameter { get; set; }
}

public sealed class ViewModelClass
{
	public ViewModelClass(string name) => Name = name;

	public string Name { get; }

	public List<ViewModelProperty> Properties { get; } = new();

	public List<ViewModelCommand> Commands { get; } = new();

	public ViewModelProperty GetOrAddProperty(string name)
	{
		var property = Properties.FirstOrDefault(p => p.Name == name);
		if (property is null)
		{
			property = new ViewModelProperty(name);
			Properties.Add(property);
		}
		return property;
	}

	public ViewModelCommand GetOrAddCommand(string bindingName)
	{
		var command = Commands.FirstOrDefault(c => c.BindingName == bindingName);
		if (command is null)
		{
			command = new ViewModelCommand(bindingName);
			Commands.Add(command);
		}
		return command;
	}
}

public static class ViewModelGenerator
{
	public const string DefaultViewClass = "MyApp.Views.MainWindow";

	public static string GetViewModelClass(DesignDocument document)
	{
		if (document.ViewModelClass is { } fromDocument)
			return fromDocument;
		return SuggestViewModelClass(document.ViewClass ?? DefaultViewClass);
	}

	public static string SuggestViewModelClass(string viewClass)
	{
		var (viewNamespace, viewName) = CodeNames.Split(viewClass);
		string stem = viewName;
		foreach (string suffix in new[] { "Window", "View", "Page", "Control", "Dialog" })
		{
			if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.Ordinal))
			{
				stem = stem[..^suffix.Length];
				break;
			}
		}
		string ns = viewNamespace.EndsWith(".Views", StringComparison.Ordinal)
			? viewNamespace[..^".Views".Length] + ".ViewModels"
			: viewNamespace.Length == 0 ? "ViewModels" : viewNamespace + ".ViewModels";
		return ns + "." + stem + "ViewModel";
	}

	public static List<ViewModelClass> Analyze(DesignDocument document)
	{
		string mainName = CodeNames.Split(GetViewModelClass(document)).Name;
		var analyzer = new Analyzer(document, mainName);
		return analyzer.Run();
	}

	public static string Generate(DesignDocument document, CodeGenOptions? options = null)
	{
		options ??= new CodeGenOptions();
		string ns = CodeNames.Split(GetViewModelClass(document)).Namespace;
		var classes = Analyze(document);

		var usings = new SortedSet<string>(StringComparer.Ordinal) { "CommunityToolkit.Mvvm.ComponentModel" };
		var all = classes.SelectMany(c => c.Properties).ToList();
		if (classes.Any(c => c.Commands.Count > 0))
		{
			usings.Add("CommunityToolkit.Mvvm.Input");
			if (classes.Any(c => c.Commands.Any(command => !command.UsesAttribute)))
				usings.Add("System.Windows.Input");
		}
		if (all.Any(p => p.IsCollection))
			usings.Add("System.Collections.ObjectModel");
		if (all.Any(p => p.Type is "IBrush?" or "IImage?"))
			usings.Add("Avalonia.Media");
		if (all.Any(p => p.Type.StartsWith("Date", StringComparison.Ordinal) || p.Type.StartsWith("TimeSpan", StringComparison.Ordinal)))
			usings.Add("System");

		var code = new StringBuilder();
		foreach (string u in CodeNames.SortUsings(usings))
			code.Append("using ").Append(u).Append(";\n");
		code.Append('\n');
		if (ns.Length > 0)
			code.Append("namespace ").Append(ns).Append(";\n\n");

		for (int i = 0; i < classes.Count; i++)
		{
			if (i > 0)
				code.Append('\n');
			WriteClass(code, classes[i], options, isMain: i == 0);
		}
		return code.ToString();
	}

	private static void WriteClass(StringBuilder code, ViewModelClass vm, CodeGenOptions options, bool isMain)
	{
		code.Append("public partial class ").Append(vm.Name).Append(" : ").Append(options.ViewModelBaseClass).Append("\n{\n");

		bool first = true;
		void Gap()
		{
			if (!first)
				code.Append('\n');
			first = false;
		}

		foreach (var property in vm.Properties)
		{
			Gap();
			if (property.Nested is not null || property.IsCollection)
			{
				code.Append("    public ").Append(property.Type).Append(' ').Append(property.Name).Append(" { get; } = new();\n");
				continue;
			}

			string type = property.Type;
			string initializer = type == "string" ? " = \"\";" : "";
			code.Append("    [ObservableProperty]\n");
			if (options.UsePartialProperties)
			{
				code.Append("    public partial ").Append(type).Append(' ').Append(property.Name).Append(" { get; set; }").Append(initializer).Append('\n');
			}
			else
			{
				string field = "_" + char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
				code.Append("    private ").Append(type).Append(' ').Append(field)
					.Append(initializer.Length > 0 ? initializer : ";").Append('\n');
			}
		}

		foreach (var command in vm.Commands)
		{
			Gap();
			string parameter = command.HasParameter ? "object? parameter" : "";
			if (command.UsesAttribute)
			{
				code.Append("    [RelayCommand]\n");
				code.Append("    private void ").Append(command.MethodName).Append('(').Append(parameter).Append(")\n");
				code.Append("    {\n    }\n");
			}
			else
			{
				string lambda = command.HasParameter ? $"new RelayCommand<object?>({command.MethodName})" : $"new RelayCommand({command.MethodName})";
				code.Append("    public ICommand ").Append(command.BindingName).Append(" => _").Append(char.ToLowerInvariant(command.BindingName[0]))
					.Append(command.BindingName[1..]).Append(" ??= ").Append(lambda).Append(";\n");
				code.Append("    private ICommand? _").Append(char.ToLowerInvariant(command.BindingName[0])).Append(command.BindingName[1..]).Append(";\n\n");
				code.Append("    private void ").Append(command.MethodName).Append('(').Append(parameter).Append(")\n");
				code.Append("    {\n    }\n");
			}
		}

		code.Append("}\n");
	}

	private sealed class Analyzer
	{
		private readonly DesignDocument _document;
		private readonly List<ViewModelClass> _classes = new();

		public Analyzer(DesignDocument document, string mainName)
		{
			_document = document;
			_classes.Add(new ViewModelClass(mainName));
		}

		public List<ViewModelClass> Run()
		{
			Walk(_document.Root, _classes[0]);
			return _classes;
		}

		private ViewModelClass GetOrAddClass(string name)
		{
			var existing = _classes.FirstOrDefault(c => c.Name == name);
			if (existing is not null)
				return existing;
			var added = new ViewModelClass(name);
			_classes.Add(added);
			return added;
		}

		private void Walk(DesignNode node, ViewModelClass? scope)
		{
			var type = XamlTypes.Resolve(_document, node);

			if (scope is not null && XamlMarkup.Parse(node.Get("DataContext")) is { IsBinding: true } contextBinding)
				scope = ResolveObject(scope, contextBinding.BindingPath);
			else if (node.Has("DataContext"))
				scope = null;

			ViewModelProperty? itemsProperty = null;
			if (scope is not null && type is not null)
			{
				var meta = XamlTypes.GetMeta(type);
				foreach (var attribute in node.Attributes.OrderBy(a => a.Name == "ItemsSource" ? 0 : 1))
				{
					if (attribute.Name == "DataContext" || XamlMarkup.Parse(attribute.Value) is not { IsBinding: true } markup)
						continue;
					var target = FindTarget(meta, type, attribute.Name);
					if (target is null)
						continue;
					var added = Register(scope, node, target, markup, itemsProperty);
					if (attribute.Name == "ItemsSource")
						itemsProperty = added;
				}
			}

			foreach (var child in node.Children)
			{
				if (IsTemplate(child))
				{
					bool isItemTemplate = node.IsPropertyElement && node.PropertyName == "ItemTemplate";
					ViewModelClass? itemScope = null;
					if (isItemTemplate && node.Parent is { } owner)
						itemScope = ItemClassFor(owner, child, scope);
					foreach (var inner in child.Children)
						Walk(inner, itemScope);
					continue;
				}
				Walk(child, scope);
			}
		}

		private bool IsTemplate(DesignNode node)
		{
			var type = XamlTypes.Resolve(_document, node);
			return type is not null && (typeof(IDataTemplate).IsAssignableFrom(type) || type.Name.EndsWith("Template", StringComparison.Ordinal));
		}

		private ViewModelClass? ItemClassFor(DesignNode itemsControl, DesignNode template, ViewModelClass? scope)
		{
			if (scope is null || XamlMarkup.Parse(itemsControl.Get("ItemsSource")) is not { IsBinding: true } binding)
				return null;
			var segments = BindingPath.Parse(binding.BindingPath);
			if (segments is not { Count: 1 })
				return null;

			string? dataType = template.Get("x:DataType") ?? template.Get("DataType");
			string className = dataType is not null && CodeNames.IsIdentifier(dataType[(dataType.IndexOf(':') + 1)..])
				? dataType[(dataType.IndexOf(':') + 1)..]
				: CodeNames.Singular(segments[0].Name) + "ViewModel";

			var property = scope.GetOrAddProperty(segments[0].Name);
			property.ItemType = className;
			return GetOrAddClass(className);
		}

		private PropertyMeta? FindTarget(TypeMeta meta, Type type, string attributeName)
		{
			int dot = attributeName.IndexOf('.');
			if (attributeName.Contains(':'))
				return null;
			if (dot < 0)
				return meta.FindProperty(attributeName);
			var owner = XamlTypes.ResolveAvalonia(attributeName[..dot]);
			var attached = owner is null ? null : XamlTypes.FindAttached(owner, attributeName[(dot + 1)..], type);
			return attached is null ? null : new PropertyMeta(attached.Name, attached.PropertyType, owner!, attached, null, true, true);
		}

		private ViewModelClass? ResolveObject(ViewModelClass scope, string? path)
		{
			var segments = BindingPath.Parse(path);
			if (segments is null)
				return null;
			var current = scope;
			foreach (var segment in segments)
			{
				if (segment.IsIndexed)
					return null;
				var property = current.GetOrAddProperty(segment.Name);
				property.Nested ??= GetOrAddClass(segment.Name + "ViewModel");
				current = property.Nested;
			}
			return current;
		}

		private ViewModelProperty? Register(ViewModelClass scope, DesignNode node, PropertyMeta target, XamlMarkup markup,
			ViewModelProperty? itemsProperty)
		{
			var segments = BindingPath.Parse(markup.BindingPath);
			if (segments is null || segments.Count == 0)
				return null;

			var owner = scope;
			for (int i = 0; i < segments.Count - 1; i++)
			{
				if (segments[i].IsIndexed)
					return null;
				var parent = owner.GetOrAddProperty(segments[i].Name);
				parent.Nested ??= GetOrAddClass(segments[i].Name + "ViewModel");
				owner = parent.Nested;
			}

			var last = segments[^1];
			var targetType = Nullable.GetUnderlyingType(target.ValueType) ?? target.ValueType;

			if (typeof(ICommand).IsAssignableFrom(targetType))
			{
				var command = owner.GetOrAddCommand(last.Name);
				command.HasParameter |= node.Has("CommandParameter");
				return null;
			}

			var property = owner.GetOrAddProperty(last.Name);
			if (last.IsIndexed)
			{
				property.ItemType ??= "object";
				return property;
			}
			if (target.Name == "ItemsSource" || (targetType != typeof(string) && targetType != typeof(object) && targetType.IsInterface
				&& typeof(IEnumerable).IsAssignableFrom(targetType)))
			{
				property.ItemType ??= "string";
				return property;
			}

			bool hasConverter = markup.Get("Converter") is not null;
			if (BindingPath.IsNegated(markup.BindingPath))
				property.AddHint("bool", 50);
			else if (hasConverter)
				property.AddHint("object?", 0);
			else if (target.Name is "SelectedItem" or "SelectedValue")
				property.AddHint(itemsProperty?.ItemType is { } item ? item + "?" : "object?", 20);
			else
				AddTypeHint(property, target.ValueType, markup);
			return property;
		}

		private static void AddTypeHint(ViewModelProperty property, Type declaredType, XamlMarkup markup)
		{
			var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
			bool nullable = type != declaredType;

			if (type == typeof(bool))
				property.AddHint("bool", 30);
			else if (type == typeof(double) || type == typeof(float))
				property.AddHint("double", 30);
			else if (type == typeof(int))
				property.AddHint("int", 30);
			else if (type == typeof(decimal))
				property.AddHint(nullable ? "decimal?" : "decimal", 30);
			else if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan))
				property.AddHint(type.Name + "?", 30);
			else if (typeof(IBrush).IsAssignableFrom(type))
				property.AddHint("IBrush?", 30);
			else if (typeof(IImage).IsAssignableFrom(type))
				property.AddHint("IImage?", 30);
			else if (type == typeof(string) || type == typeof(object))
			{
				string? format = markup.Get("StringFormat");
				if (format is not null && LooksNumeric(format))
					property.AddHint("double", 25);
				else
					property.AddHint("string", 10);
			}
			else
				property.AddHint("object?", 1);
		}

		private static bool LooksNumeric(string format)
		{
			string text = XamlMarkup.Unescape(format.Trim('\''));
			int colon = text.IndexOf("{0:", StringComparison.Ordinal);
			if (colon < 0)
				return false;
			char kind = colon + 3 < text.Length ? char.ToUpperInvariant(text[colon + 3]) : ' ';
			return kind is 'F' or 'N' or 'P' or 'E' or 'G' or '0' or '#' or 'C';
		}
	}
}

public static class BindingPath
{
	public sealed record Segment(string Name, bool IsIndexed);

	public static bool IsNegated(string? path) => path is not null && path.TrimStart().StartsWith('!');

	public static List<Segment>? Parse(string? path)
	{
		if (path is null)
			return null;
		string text = path.Trim().TrimStart('!').Trim();
		var segments = new List<Segment>();
		if (text.Length == 0 || text == ".")
			return segments;
		if (text[0] is '#' or '$' or '(')
			return null;

		foreach (string raw in text.Split('.'))
		{
			string part = raw.Trim().TrimEnd('^');
			bool indexed = false;
			int bracket = part.IndexOf('[');
			if (bracket >= 0)
			{
				indexed = true;
				part = part[..bracket];
			}
			if (!CodeNames.IsIdentifier(part))
				return null;
			segments.Add(new Segment(part, indexed));
			if (indexed)
				break;
		}
		return segments;
	}
}

public static class CodeNames
{
	private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
	{
		"abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
		"continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
		"false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
		"internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
		"params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
		"sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
		"uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
	};

	public static bool IsIdentifier(string? text)
	{
		if (string.IsNullOrEmpty(text) || Keywords.Contains(text))
			return false;
		if (!(char.IsLetter(text[0]) || text[0] == '_'))
			return false;
		return text.All(c => char.IsLetterOrDigit(c) || c == '_');
	}

	public static bool IsQualifiedName(string? text) =>
		!string.IsNullOrEmpty(text) && text.Split('.').All(IsIdentifier);

	public static (string Namespace, string Name) Split(string fullName)
	{
		int dot = fullName.LastIndexOf('.');
		return dot < 0 ? ("", fullName) : (fullName[..dot], fullName[(dot + 1)..]);
	}

	public static IEnumerable<string> SortUsings(IEnumerable<string> usings) =>
		usings.Distinct()
			.OrderBy(u => u == "System" || u.StartsWith("System.", StringComparison.Ordinal) ? 0 : 1)
			.ThenBy(u => u, StringComparer.Ordinal);

	public static string Singular(string plural)
	{
		if (plural.Length > 4 && plural.EndsWith("ies", StringComparison.Ordinal))
			return plural[..^3] + "y";
		if (plural.Length > 3 && plural.EndsWith('s') && !plural.EndsWith("ss", StringComparison.Ordinal))
			return plural[..^1];
		return plural + "Item";
	}
}
