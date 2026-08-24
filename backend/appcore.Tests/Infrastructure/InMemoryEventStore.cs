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
			.Where(e => EventPayload.Matches(e, boundary))
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
			EventPayload.Validate(EventSerializer.Serialize(domainEvent));

		var conflicted = _events.Any(e => e.SequenceId > boundary.LastPosition && EventPayload.Matches(e, boundary.Selectors.ToArray()));
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
}

internal static class TestContext
{
	public static EventContext From(params AppEvent[] events)
	{
		var head = events.Length > 0 ? events.Max(e => e.SequenceId) : 0L;
		return new EventContext(events, head);
	}
}
