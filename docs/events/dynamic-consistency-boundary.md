# Dynamic Consistency Boundary

The event-sourcing write path uses a **Dynamic Consistency Boundary (DCB)** instead of fixed aggregates. A DCB lets each incoming command decide *which* events form the consistency boundary for its own decision, rather than binding every operation to a pre-declared aggregate/stream.

The pattern is generic across use cases — auction lifecycle events, user signup, bids, etc. Nothing in the machinery knows about domains.

## Principles

1. **Lock-free information gathering.** The handler reads the context the decision needs *before* any lock is taken. Slow decisions never block writers.
2. **One query per decision round.** The decision describes its context needs declaratively (event types + payload constraints). The handler performs exactly one read for the whole accumulated scope, then asks the decision to evaluate. If the decision realizes it needs more context, the handler widens the scope and re-reads the *entire* scope (a full reload — never incremental merges).
3. **Sequential consistency by retry.** The result is committed only if the payload set the decision was based on hasn't changed since gathering. If it has, a conflict is detected and the whole pipeline is retried with freshly loaded context. Retries converge to a sequential execution order.
4. **Scopes are live JSON predicates.** A scope is re-evaluated against the database at insert time. Writers do **not** pre-declare tags or streams; every top-level scalar JSON property of an event payload is implicitly a dimension that any query may constrain. Indexes can be added per hot path later without interface changes.
5. **No serializable transactions.** Isolation is READ COMMITTED. Atomicity of "check + insert" is provided by transaction-scoped advisory locks derived from the payload dimensions the operation reads *and* writes.
6. **Invariant violations abort, never retry.** Domain validation failures (`InvariantViolation`) propagate immediately. Only concurrency conflicts and transient DB errors trigger the retry loop.

## Terminology

| Term | Meaning |
|---|---|
| `EventSelector` | One conjunctive term: a set of event types **and** equality constraints on payload properties. |
| `ConsistencyBoundary` | The disjunction of selectors consulted by a decision, plus `LastPosition`. |
| `LastPosition` | `MAX(sequence_id)` over the events matching the boundary's selectors at the most recent read (`0` when none match). |
| `EventContext` | Immutable snapshot: the events matching the boundary plus its `Head`. |
| Dimension | A top-level scalar property of an event payload (e.g. `auctionId`, `userId`). Queries constrain dimensions; events carry them implicitly. |

## Interfaces

```csharp
public sealed record DimensionConstraint(string Property, string Value);

public sealed record EventSelector(
    IReadOnlyCollection<string> EventTypes,
    IReadOnlyCollection<DimensionConstraint> Constraints)
{
    public static EventSelector ForAuction(Guid auctionId, params string[] eventTypes);
    public static EventSelector ForUser(Guid userId, params string[] eventTypes);
    public static EventSelector OfTypes(params string[] eventTypes);
}

public sealed record EventContext(IReadOnlyList<AppEvent> Events, long Head)
{
    public TState Fold<TState>(TState seed, Func<TState, StoredEvent, TState> fold);
}

public interface IEventReader
{
    // Single SELECT over the whole boundary:
    // WHERE (types ∧ constraints) OR (types ∧ constraints) ... ORDER BY sequence_id
    Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct);
}

public sealed record ConsistencyBoundary(IReadOnlyList<EventSelector> Selectors, long LastPosition)
{
    public static ConsistencyBoundary StartWith(EventSelector initial, long head);
    public ConsistencyBoundary Include(EventSelector added);
}

public sealed class ConcurrencyConflictException : Exception;

public interface IEventStore
{
    IEventReader Reader { get; }
    // Appends iff no event matching boundary.Selectors exists with sequence_id > boundary.LastPosition.
    // Throws ConcurrencyConflictException otherwise; caller retries with fresh context.
    Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct);
}

public abstract record DecisionStep<TResult>
{
    public sealed record NeedMoreContext(EventSelector AdditionalSelector) : DecisionStep<TResult>;
    public sealed record Complete(TResult Value, IReadOnlyList<StoredEvent> EventsToAppend) : DecisionStep<TResult>;
}

public interface IDecisionFunction<in TInput, TResult>
{
    EventSelector InitialSelector(TInput input);
    DecisionStep<TResult> Step(TInput input, EventContext context);   // synchronous, no I/O
}

public sealed record EventHandlerOptions
{
    public int MaxAttempts { get; init; } = 10;
    public int MaxExpansions { get; init; } = 10;
}
```

## Handler loop

```
for attempt in 1..MaxAttempts:
    boundary = StartWith(decision.InitialSelector(input), head: 0)
    ctx      = reader.Read(boundary.Selectors)          # lock-free
    boundary.LastPosition = ctx.Head

    for expansion in 1..MaxExpansions:
        match decision.Step(input, ctx):
            NeedMoreContext(extra):
                boundary.Include(extra)
                ctx = reader.Read(boundary.Selectors)   # FULL reload
                boundary.LastPosition = ctx.Head
            Complete(value, events):
                store.Append(events, boundary)          # may throw ConcurrencyConflictException
                return value

    # conflict / transient error → back off, retry from the top with fresh context
```

## Append protocol (PostgreSQL, READ COMMITTED)

```
BEGIN;
-- 1. Acquire advisory locks in canonical (sorted) order to avoid deadlocks.
--    Keys = read-side: every (property, value) constrained by boundary.Selectors
--          ∪ write-side: every top-level scalar (property, value) of each inserted event.
foreach (property, value) in keys.OrderBy(k => k):
    SELECT pg_advisory_xact_lock(@stable_hash(property, value));

-- 2. Re-evaluate the scope NOW, against everything committed so far.
--    A matched event with a higher sequence id means the loaded context is stale.
IF EXISTS (SELECT 1 FROM auction_events
           WHERE sequence_id > @last_position
             AND (dnf predicate built from boundary.Selectors))
   → throw ConcurrencyConflictException;  -- rollback

-- 3. Insert the result events and commit (advisory locks released automatically).
INSERT INTO auction_events (insertion_time, event_type, payload) VALUES ...;
COMMIT;
```

The locks exist solely to stop two concurrent appends from both passing the existence check (write skew under READ COMMITTED). They are held only for the duration of the append — never during information gathering. Unrelated partitions do not share keys and do not contend; only genuine scope overlap serializes.

Roundtrips are minimized: all advisory locks plus the scope re-check are sent as a single `NpgsqlBatch` (check last, so it runs after all locks are held), and all inserts go out in a second single batch — two roundtrips per append regardless of the number of lock keys or events.

## Soundness

- **Read-side locking:** every operation locks every partition it consulted, so two operations whose scopes can observe each other's writes always contend on a shared key.
- **Write-side locking:** because every top-level scalar property is a dimension, any event that *matches* a selector necessarily carries the `(property, value)` pairs of that selector's constraints — so its inserter locks a key the reader holds. Cross-partition outputs (e.g. a `BidPlaced` decision emitting an `OwnerNotified` event for a different user) are still protected: the write-side lock derived from the notification's own properties makes readers of *that* partition serialize against us.
- **Position check:** `LastPosition` is the `MAX(sequence_id)` over the events that matched the boundary at the most recent read (`0` when none matched — any matching event then invalidates). The read returns the complete matched set, so every matched event in the loaded context has `sequence_id ≤ LastPosition`; any event matching the boundary but appended concurrently has `sequence_id > LastPosition` (the global sequence is monotonic) and is therefore detected.
- **Event validity:** event payloads must be JSON objects, and every persisted event must carry at least one top-level scalar property (a property-less event could never be matched by any scope and would silently live outside all consistency boundaries).

## Conflict handling

A `ConcurrencyConflictException` restarts the entire pipeline — the decision is re-run against a freshly loaded context. This is what provides sequential consistency: concurrent operations may interleave arbitrarily, but every committed outcome is the result of a decision made against a context that was still current at commit time, and retries converge to the order in which the appends actually committed.

## Notes

- **Dimension matching is textual.** Scope predicates compare `payload->>'prop' = @value` as text. Guid/string values compare exactly; numeric values depend on serialization (e.g. `10.00m` serializes as `"10.00"`). Constrain numeric dimensions with care — use consistently formatted values or prefer string-typed dimensions.
- **Lock keys are stable hashes.** `(property, value)` pairs are hashed with SHA1 (first 8 bytes) to the `bigint` advisory-lock key; collisions only cause extra serialization, never incorrectness.

## Test strategy

An in-memory `IEventStore` mirroring the append semantics (conflict detection, sequence-id assignment, payload validation) provides deterministic test doubles. Concurrency conflicts are injected directly rather than simulated through threads, so the retry loop is exercised without flakiness.
