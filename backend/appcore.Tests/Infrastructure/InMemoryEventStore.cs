using System.Text.Json;
using appcore.Entities;
using appcore.Infra;

namespace appcore.Tests.Infrastructure;

internal sealed class InMemoryEventStore : IEventStore, IEventReader
{
	private readonly List<AppEvent> _events = [];

	public InMemoryEventStore(IEnumerable<AppEvent>? seed = null)
	{
		if (seed is not null)
			_events.AddRange(seed);
	}

	public IEventReader Reader => this;

	public IReadOnlyList<AppEvent> Events => _events;

	public void Seed(IEnumerable<AppEvent> events) => _events.AddRange(events);

	public Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct)
	{
		var matching = _events
			.Where(e => Matches(e, boundary))
			.OrderBy(e => e.SequenceId)
			.ToList();
		var head = _events.Count > 0 ? _events.Max(e => e.SequenceId) : 0L;
		return Task.FromResult(new EventContext(matching, head));
	}

	public Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
	{
		if (events.Count == 0)
			return Task.CompletedTask;

		foreach (var domainEvent in events)
		{
			var row = EventSerializer.Serialize(domainEvent);
			if (row.Payload.RootElement.ValueKind != JsonValueKind.Object)
				throw new InvalidOperationException($"Event {row.EventType} must serialize to a JSON object.");
			if (ScalarCount(row.Payload.RootElement) == 0)
				throw new InvalidOperationException($"Event {row.EventType} must carry at least one top-level scalar property.");
		}

		var conflicted = _events.Any(e => e.SequenceId > boundary.LastPosition && Matches(e, boundary.Selectors.ToArray()));
		if (conflicted)
			throw new ConcurrencyConflictException();

		var next = (_events.Count > 0 ? _events.Max(e => e.SequenceId) : 0) + 1;
		foreach (var domainEvent in events)
		{
			var row = EventSerializer.Serialize(domainEvent);
			_events.Add(row with { SequenceId = next++ });
		}
		return Task.CompletedTask;
	}

	private static bool Matches(AppEvent e, EventSelector[] boundary)
	{
		foreach (var selector in boundary)
		{
			if (!selector.EventTypes.Contains(e.EventType))
				continue;

			var allMatch = true;
			foreach (var constraint in selector.Constraints)
			{
				if (!e.Payload.RootElement.TryGetProperty(constraint.Property, out var prop)
					|| ScalarText(prop) != constraint.Value)
				{
					allMatch = false;
					break;
				}
			}
			if (allMatch)
				return true;
		}
		return false;
	}

	private static int ScalarCount(JsonElement root)
	{
		var count = 0;
		foreach (var property in root.EnumerateObject())
		{
			if (IsScalar(property.Value))
				count++;
		}
		return count;
	}

	private static bool IsScalar(JsonElement element) => element.ValueKind
		is JsonValueKind.String
		or JsonValueKind.Number
		or JsonValueKind.True
		or JsonValueKind.False;

	private static string? ScalarText(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.String => element.GetString(),
		JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
		_ => null,
	};
}

internal static class TestContext
{
	public static EventContext From(params AppEvent[] events)
	{
		var head = events.Length > 0 ? events.Max(e => e.SequenceId) : 0L;
		return new EventContext(events, head);
	}
}
