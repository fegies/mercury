using appcore.Entities;

namespace appcore.Infra;

public sealed record EventContext(IReadOnlyList<AppEvent> Events, long Head)
{
	public TState Fold<TState>(TState seed, Func<TState, long, StoredEvent, TState> fold)
	{
		var state = seed;
		foreach (var row in Events)
			state = fold(state, row.SequenceId, EventSerializer.Deserialize(row));
		return state;
	}
}

public interface IEventReader
{
	// Single SELECT over the whole boundary (disjunction of selectors), ordered by sequence_id.
	// Head is the MAX(sequence_id) over the matched events (0 when none match).
	Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct);
}

public sealed record ConsistencyBoundary(IReadOnlyList<EventSelector> Selectors, long LastPosition)
{
	public static ConsistencyBoundary StartWith(EventSelector initial, long head)
		=> new([initial], head);

	public ConsistencyBoundary Include(EventSelector added) => this with
	{
		Selectors = [.. Selectors, added],
	};
}

public sealed class ConcurrencyConflictException : Exception;

public interface IEventStore
{
	IEventReader Reader { get; }

	// Appends iff no event matching boundary.Selectors exists with sequence_id > boundary.LastPosition.
	// Throws ConcurrencyConflictException otherwise; the caller retries with a freshly loaded context.
	Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct);
}
