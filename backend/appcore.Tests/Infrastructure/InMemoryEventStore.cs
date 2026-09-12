using appcore.Entities;
using appcore.Infra;
using appcore.Infra.Events;

namespace appcore.Tests.Infrastructure;

internal sealed class InMemoryEventStore : IEventStore, IEventReader
{
	private readonly List<AppEvent> _events = [];
	private readonly IAppBus? _bus;

	public InMemoryEventStore(IEnumerable<AppEvent>? seed = null, IAppBus? bus = null)
	{
		_bus = bus;
		if (seed is not null)
			_events.AddRange(seed);
	}

	public IEventReader Reader => this;

	public IReadOnlyList<AppEvent> Events => _events;

	public void Seed(IEnumerable<AppEvent> events) => _events.AddRange(events);

	public Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct)
	{
		var matching = _events
			.Where(e => EventPayload.Matches(e, boundary))
			.OrderBy(e => e.SequenceId)
			.ToList();
		var head = matching.Count > 0 ? matching.Max(e => e.SequenceId) : 0L;
		return Task.FromResult(new EventContext(boundary, matching, head));
	}

	public async Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
	{
		if (events.Count == 0)
			return;

		foreach (var domainEvent in events)
			EventPayload.Validate(EventSerializer.Serialize(domainEvent));

		var conflicted = _events.Any(e => e.SequenceId > boundary.LastPosition && EventPayload.Matches(e, boundary.Selectors.ToArray()));
		if (conflicted)
			throw new ConcurrencyConflictException();

		var next = (_events.Count > 0 ? _events.Max(e => e.SequenceId) : 0) + 1;
		var appended = new List<StoredEvent>(events.Count);
		foreach (var domainEvent in events)
		{
			var row = EventSerializer.Serialize(domainEvent);
			_events.Add(row with { SequenceId = next++ });
			appended.Add(domainEvent);
		}

		if (_bus is not null)
			foreach (var domainEvent in appended)
				await _bus.EmitAsync(domainEvent, ct);
	}
}

internal static class TestContext
{
	public static EventContext From(params AppEvent[] events)
	{
		return From([], events);
	}

	public static EventContext From(EventSelector[] query, params AppEvent[] events)
	{
		var head = events.Length > 0 ? events.Max(e => e.SequenceId) : 0L;
		return new EventContext(query, events, head);
	}
}
