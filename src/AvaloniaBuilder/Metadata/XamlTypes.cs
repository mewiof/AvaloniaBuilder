using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Metadata;

public static class XamlTypes
{
	private static readonly Lazy<Dictionary<string, Type>> AvaloniaTypes = new(BuildAvaloniaTypes);
	private static readonly ConcurrentDictionary<Type, TypeMeta> Metas = new();

	private static readonly Dictionary<string, Type> XamlPrimitives = new(StringComparer.Ordinal)
	{
		["String"] = typeof(string),
		["Double"] = typeof(double),
		["Single"] = typeof(float),
		["Int32"] = typeof(int),
		["Int64"] = typeof(long),
		["Int16"] = typeof(short),
		["Byte"] = typeof(byte),
		["Boolean"] = typeof(bool),
		["Decimal"] = typeof(decimal),
		["Char"] = typeof(char),
		["Object"] = typeof(object),
		["TimeSpan"] = typeof(TimeSpan),
		["Uri"] = typeof(Uri),
	};

	private static IEnumerable<Assembly> AvaloniaAssemblies()
	{
		yield return typeof(Control).Assembly;
		yield return typeof(Visual).Assembly;
		yield return typeof(Avalonia.Markup.Xaml.AvaloniaXamlLoader).Assembly;
		yield return typeof(Avalonia.Themes.Fluent.FluentTheme).Assembly;
		yield return typeof(ColorView).Assembly;
		foreach (string name in new[] { "Avalonia.Markup" })
		{
			Assembly? assembly = null;
			try
			{
				assembly = Assembly.Load(name);
			}
			catch (Exception)
			{
			}
			if (assembly is not null)
				yield return assembly;
		}
	}

	private static Dictionary<string, Type> BuildAvaloniaTypes()
	{
		var types = new Dictionary<string, Type>(StringComparer.Ordinal);
		foreach (var assembly in AvaloniaAssemblies().Distinct())
		{
			var clrNamespaces = assembly.GetCustomAttributes<XmlnsDefinitionAttribute>()
				.Where(a => a.XmlNamespace == XamlNs.Avalonia)
				.Select(a => a.ClrNamespace)
				.ToHashSet(StringComparer.Ordinal);
			if (clrNamespaces.Count == 0)
				continue;
			foreach (var type in assembly.GetExportedTypes())
			{
				if (type.IsNested || type.IsGenericTypeDefinition || type.Namespace is null)
					continue;
				if (clrNamespaces.Contains(type.Namespace))
					types.TryAdd(type.Name, type);
			}
		}
		return types;
	}

	public static Type? ResolveAvalonia(string typeName) => AvaloniaTypes.Value.GetValueOrDefault(typeName);

	public static Type? Resolve(DesignDocument? document, string prefix, string typeName)
	{
		if (typeName.Contains('.'))
			return null;

		string? uri = document?.GetNamespaceUri(prefix);
		if (uri is null)
		{
			if (prefix.Length == 0)
				uri = XamlNs.Avalonia;
			else if (prefix == "x")
				uri = XamlNs.Xaml;
			else
				return null;
		}

		if (uri == XamlNs.Avalonia)
			return ResolveAvalonia(typeName);
		if (uri == XamlNs.Xaml)
			return XamlPrimitives.GetValueOrDefault(typeName);
		if (XamlNs.ClrNamespaceOf(uri) is { } clrNamespace)
			return ResolveClr(clrNamespace, typeName);
		return null;
	}

	public static Type? Resolve(DesignDocument? document, DesignNode node) => Resolve(document, node.Prefix, node.TypeName);

	private static Type? ResolveClr(string clrNamespace, string typeName)
	{
		string fullName = clrNamespace + "." + typeName;
		if (clrNamespace == "System" || clrNamespace.StartsWith("System.", StringComparison.Ordinal))
			return typeof(object).Assembly.GetType(fullName) ?? typeof(Uri).Assembly.GetType(fullName);
		if (clrNamespace == "Avalonia" || clrNamespace.StartsWith("Avalonia.", StringComparison.Ordinal))
		{
			foreach (var assembly in AvaloniaAssemblies())
				if (assembly.GetType(fullName) is { IsPublic: true } type)
					return type;
		}
		return null;
	}

	public static TypeMeta GetMeta(Type type) => Metas.GetOrAdd(type, t => new TypeMeta(t));

	public static AvaloniaProperty? FindAttached(Type ownerType, string name, Type targetType)
	{
		if (typeof(AvaloniaObject).IsAssignableFrom(targetType))
		{
			foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(targetType))
				if (property.Name == name && property.OwnerType == ownerType)
					return property;
		}
		if (typeof(AvaloniaObject).IsAssignableFrom(ownerType))
			return AvaloniaPropertyRegistry.Instance.FindRegistered(ownerType, name);
		return null;
	}

	public static IEnumerable<PropertyMeta> GetAttached(Type ownerType, Type targetType)
	{
		if (!typeof(AvaloniaObject).IsAssignableFrom(targetType))
			yield break;
		foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(targetType))
		{
			if (property.OwnerType == ownerType && !property.IsReadOnly)
				yield return new PropertyMeta(property.Name, property.PropertyType, ownerType, property, null, true, isAttached: true);
		}
	}

	public static IReadOnlyCollection<Type> AllAvaloniaTypes => AvaloniaTypes.Value.Values;
}
