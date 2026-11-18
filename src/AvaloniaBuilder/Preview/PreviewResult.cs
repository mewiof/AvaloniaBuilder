using Avalonia.Controls;
using AvaloniaBuilder.Metadata;
using AvaloniaBuilder.Model;

namespace AvaloniaBuilder.Preview;

public enum DiagnosticLevel
{
	Info,
	Warning,
	Error,
}

public sealed record PreviewDiagnostic(DiagnosticLevel Level, DesignNode Node, string? Attribute, string Message);

public sealed class PreviewOptions
{
	public bool DesignMode { get; init; } = true;

	public DesignNode? Selected { get; init; }

	public Dictionary<DesignNode, int> ActiveTabs { get; init; } = new();

	public ValueContext? ValueContext { get; init; }
}

public sealed class PreviewResult : IDisposable
{
	private readonly List<Action> _cleanup;

	internal PreviewResult(Control? root, Dictionary<DesignNode, object> objects, List<PreviewDiagnostic> diagnostics,
		HashSet<DesignNode> emptyContainers, List<Action> cleanup)
	{
		Root = root;
		Objects = objects;
		Diagnostics = diagnostics;
		EmptyContainers = emptyContainers;
		_cleanup = cleanup;
	}

	public Control? Root { get; }

	public IReadOnlyDictionary<DesignNode, object> Objects { get; }

	public IReadOnlyList<PreviewDiagnostic> Diagnostics { get; }

	public IReadOnlySet<DesignNode> EmptyContainers { get; }

	public Control? GetControl(DesignNode node) => Objects.TryGetValue(node, out var value) ? value as Control : null;

	public void Dispose()
	{
		foreach (var action in _cleanup)
		{
			try
			{
				action();
			}
			catch (Exception)
			{
			}
		}
		_cleanup.Clear();
	}
}
