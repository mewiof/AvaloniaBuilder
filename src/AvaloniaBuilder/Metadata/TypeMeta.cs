using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace AvaloniaBuilder.Metadata;

public sealed class PropertyMeta
{
	public PropertyMeta(string name, Type valueType, Type declaringType, AvaloniaProperty? avaloniaProperty,
		PropertyInfo? clrProperty, bool canWrite, bool isAttached = false)
	{
		Name = name;
		ValueType = valueType;
		DeclaringType = declaringType;
		AvaloniaProperty = avaloniaProperty;
		ClrProperty = clrProperty;
		CanWrite = canWrite;
		IsAttached = isAttached;
	}

	public string Name { get; }

	public Type ValueType { get; }

	public Type DeclaringType { get; }

	public AvaloniaProperty? AvaloniaProperty { get; }

	public PropertyInfo? ClrProperty { get; }

	public bool CanWrite { get; }

	public bool IsAttached { get; }

	public string XamlName => IsAttached ? DeclaringType.Name + "." + Name : Name;

	public object? GetValue(object target)
	{
		if (AvaloniaProperty is not null && target is AvaloniaObject avaloniaObject)
			return avaloniaObject.GetValue(AvaloniaProperty);
		return ClrProperty?.GetValue(target);
	}

	public void SetValue(object target, object? value)
	{
		if (AvaloniaProperty is { IsReadOnly: false } && target is AvaloniaObject avaloniaObject)
			avaloniaObject.SetValue(AvaloniaProperty, value);
		else if (ClrProperty?.SetMethod is { IsPublic: true })
			ClrProperty.SetValue(target, value);
		else
			throw new InvalidOperationException($"The property {Name} cannot be set.");
	}

	public override string ToString() => $"{DeclaringType.Name}.{Name} : {ValueType.Name}";
}

public sealed class EventMeta
{
	public EventMeta(string name, Type declaringType, Type argsType)
	{
		Name = name;
		DeclaringType = declaringType;
		ArgsType = argsType;
	}

	public string Name { get; }

	public Type DeclaringType { get; }

	public Type ArgsType { get; }
}

public sealed class TypeMeta
{
	private readonly Dictionary<string, PropertyMeta> _properties = new(StringComparer.Ordinal);
	private readonly Dictionary<string, EventMeta> _events = new(StringComparer.Ordinal);

	internal TypeMeta(Type type)
	{
		Type = type;
		IsControl = typeof(Control).IsAssignableFrom(type);
		CanCreate = !type.IsAbstract && !type.IsInterface && type.GetConstructor(Type.EmptyTypes) is not null;
		bool isAvaloniaObject = typeof(AvaloniaObject).IsAssignableFrom(type);

		PropertyMeta? content = null;
		int contentDepth = -1;
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
				continue;
			if (_properties.TryGetValue(property.Name, out var existing)
				&& Depth(existing.DeclaringType) >= Depth(property.DeclaringType!))
				continue;

			AvaloniaProperty? avaloniaProperty = null;
			if (isAvaloniaObject)
			{
				avaloniaProperty = AvaloniaPropertyRegistry.Instance.FindRegistered(type, property.Name);
				if (avaloniaProperty is not null && !avaloniaProperty.PropertyType.IsAssignableFrom(property.PropertyType)
					&& !property.PropertyType.IsAssignableFrom(avaloniaProperty.PropertyType))
					avaloniaProperty = null;
			}

			bool canWrite = property.SetMethod is { IsPublic: true } || avaloniaProperty is { IsReadOnly: false };
			var meta = new PropertyMeta(property.Name, property.PropertyType, property.DeclaringType!, avaloniaProperty, property, canWrite);
			_properties[property.Name] = meta;

			if (property.GetCustomAttribute<ContentAttribute>(inherit: true) is not null)
			{
				int depth = Depth(property.DeclaringType!);
				if (depth > contentDepth)
				{
					content = meta;
					contentDepth = depth;
				}
			}
		}
		ContentProperty = content;
		Properties = _properties.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

		foreach (var info in type.GetEvents(BindingFlags.Public | BindingFlags.Instance))
		{
			var invoke = info.EventHandlerType?.GetMethod("Invoke");
			var parameters = invoke?.GetParameters();
			if (parameters is not { Length: 2 })
				continue;
			_events[info.Name] = new EventMeta(info.Name, info.DeclaringType!, parameters[1].ParameterType);
		}
		Events = _events.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
	}

	public Type Type { get; }

	public bool IsControl { get; }

	public bool CanCreate { get; }

	public PropertyMeta? ContentProperty { get; }

	public IReadOnlyList<PropertyMeta> Properties { get; }

	public IReadOnlyList<EventMeta> Events { get; }

	public PropertyMeta? FindProperty(string name) => _properties.GetValueOrDefault(name);

	public EventMeta? FindEvent(string name) => _events.GetValueOrDefault(name);

	private static int Depth(Type type)
	{
		int depth = 0;
		for (var t = type; t is not null; t = t.BaseType)
			depth++;
		return depth;
	}
}
