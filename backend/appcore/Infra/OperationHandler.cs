using System.Text;
using System.Text.Json;
using appcore.Data;
using appcore.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace appcore.Infra;

public static class EventTypeNames
{
	public const string AuctionCreated = nameof(Entities.Events.AuctionCreated);
	public const string AuctionUpdated = nameof(Entities.Events.AuctionUpdated);
	public const string AuctionImagesAdded = nameof(Entities.Events.AuctionImagesAdded);
	public const string AuctionImagesRemoved = nameof(Entities.Events.AuctionImagesRemoved);
	public const string AuctionClosed = nameof(Entities.Events.AuctionClosed);

	public static readonly string[] Auction = [AuctionCreated, AuctionUpdated, AuctionImagesAdded, AuctionImagesRemoved, AuctionClosed];
}

public sealed record DimensionConstraint(string Property, string Value);

public sealed record EventSelector(
	IReadOnlyCollection<string> EventTypes,
	IReadOnlyCollection<DimensionConstraint> Constraints)
{
	public static EventSelector ForAuction(Guid auctionId, params string[] eventTypes)
		=> new(eventTypes, [new DimensionConstraint("auctionId", auctionId.ToString())]);

	public static EventSelector ForUser(Guid userId, params string[] eventTypes)
		=> new(eventTypes, [new DimensionConstraint("userId", userId.ToString())]);

	public static EventSelector OfTypes(params string[] eventTypes)
		=> new(eventTypes, []);
}

public sealed record EventContext(IReadOnlyList<AppEvent> Events, long Head)
{
	public TState Fold<TState>(TState seed, Func<TState, StoredEvent, TState> fold)
	{
		var state = seed;
		foreach (var row in Events)
			state = fold(state, EventSerializer.Deserialize(row));
		return state;
	}
}

public interface IEventReader
{
	// Single SELECT over the whole boundary (disjunction of selectors), ordered by sequence_id.
	// Head is the global MAX(sequence_id) as of the same snapshot.
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

public abstract record DecisionStep<TResult>
{
	public sealed record NeedMoreContext(EventSelector AdditionalSelector) : DecisionStep<TResult>;
	public sealed record Complete(TResult Value, IReadOnlyList<StoredEvent> EventsToAppend) : DecisionStep<TResult>;
}

public interface IDecisionFunction<in TInput, TResult>
{
	EventSelector InitialSelector(TInput input);
	DecisionStep<TResult> Step(TInput input, EventContext context);
}

public sealed record EventHandlerOptions
{
	public int MaxAttempts { get; init; } = 10;
	public int MaxExpansions { get; init; } = 10;
}

public sealed class IncomingEventHandler<TInput, TResult>(
	IEventStore store,
	IDecisionFunction<TInput, TResult> decision,
	EventHandlerOptions options)
{
	public async Task<TResult> Execute(TInput input, CancellationToken ct)
	{
		for (var attempt = 0; attempt < options.MaxAttempts; attempt++)
		{
			try
			{
				return await TryExecute(input, ct);
			}
			catch (ConcurrencyConflictException) when (attempt < options.MaxAttempts - 1)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt)), ct);
			}
			catch (NpgsqlException) when (attempt < options.MaxAttempts - 1)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt)), ct);
			}
		}

		throw new InvalidOperationException("Event handler did not converge after repeated attempts.");
	}

	private async Task<TResult> TryExecute(TInput input, CancellationToken ct)
	{
		var reader = store.Reader;
		var boundary = ConsistencyBoundary.StartWith(decision.InitialSelector(input), head: 0);
		var context = await reader.Read(boundary.Selectors.ToArray(), ct);
		boundary = boundary with { LastPosition = context.Head };

		for (var expansion = 0; expansion < options.MaxExpansions; expansion++)
		{
			ct.ThrowIfCancellationRequested();

			switch (decision.Step(input, context))
			{
				case DecisionStep<TResult>.NeedMoreContext more:
					boundary = boundary.Include(more.AdditionalSelector);
					context = await reader.Read(boundary.Selectors.ToArray(), ct);
					boundary = boundary with { LastPosition = context.Head };
					continue;

				case DecisionStep<TResult>.Complete done:
					await store.Append(done.EventsToAppend, boundary, ct);
					return done.Value;
			}
		}

		throw new InvalidOperationException("Decision did not converge within the expansion limit.");
	}
}

public class DbEventStore : IEventStore, IEventReader
{
	internal readonly NpgsqlConnection Connection;

	public DbEventStore(ApplicationDbContext context)
	{
		Connection = context.Database.GetDbConnection() as NpgsqlConnection
			?? throw new InvalidOperationException("Expected NpgsqlConnection");
	}

	public IEventReader Reader => this;

	public async Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct)
	{
		await Connection.OpenAsync(ct);

		var (predicate, parameters) = BuildBoundaryPredicate(boundary);

		await using var cmd = Connection.CreateCommand();
		cmd.CommandText = $"""
			SELECT sequence_id, insertion_time, event_type, payload,
			       (SELECT COALESCE(MAX(sequence_id), 0) FROM auction_events) AS head
			FROM auction_events
			WHERE {predicate}
			ORDER BY sequence_id
			""";
		foreach (var parameter in parameters)
			cmd.Parameters.Add(parameter);

		var rows = new List<AppEvent>();
		var head = 0L;
		await using var reader = await cmd.ExecuteReaderAsync(ct);
		while (await reader.ReadAsync(ct))
		{
			rows.Add(new AppEvent
			{
				SequenceId = reader.GetInt64(0),
				InsertionTime = reader.GetDateTime(1),
				EventType = reader.GetString(2),
				Payload = JsonDocument.Parse(reader.GetString(3)),
			});
			head = reader.GetInt64(4);
		}

		return new EventContext(rows, head);
	}

	public async Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
	{
		if (events.Count == 0)
			return;

		await Connection.OpenAsync(ct);

		var serialized = new List<AppEvent>(events.Count);
		var writeKeys = new List<(string Property, string Value)>();
		foreach (var domainEvent in events)
		{
			var row = EventSerializer.Serialize(domainEvent);
			ValidatePayload(row);
			writeKeys.AddRange(ExtractScalarDimensions(row.Payload.RootElement));
			serialized.Add(row);
		}

		var readKeys = boundary.Selectors
			.SelectMany(s => s.Constraints)
			.Select(c => (c.Property, c.Value));
		var lockKeys = readKeys
			.Concat(writeKeys)
			.Distinct()
			.OrderBy(k => k.Property, StringComparer.Ordinal)
			.ThenBy(k => k.Value, StringComparer.Ordinal)
			.ToList();

		await using var trans = await Connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

		foreach (var (property, value) in lockKeys)
		{
			await using var lockCmd = Connection.CreateCommand();
			lockCmd.Transaction = trans;
			lockCmd.CommandText = "SELECT pg_advisory_xact_lock(@key)";
			lockCmd.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bigint) { Value = StableHash(property, value) });
			await lockCmd.ExecuteNonQueryAsync(ct);
		}

		var (predicate, parameters) = BuildBoundaryPredicate(boundary.Selectors.ToArray());
		await using var checkCmd = Connection.CreateCommand();
		checkCmd.Transaction = trans;
		checkCmd.CommandText = $"""
			SELECT EXISTS (
				SELECT 1 FROM auction_events
				WHERE sequence_id > @head AND ({predicate})
			)
			""";
		checkCmd.Parameters.Add(new NpgsqlParameter("head", NpgsqlDbType.Bigint) { Value = boundary.LastPosition });
		foreach (var parameter in parameters)
			checkCmd.Parameters.Add(parameter);

		var conflicted = (bool)(await checkCmd.ExecuteScalarAsync(ct))!;
		if (conflicted)
			throw new ConcurrencyConflictException();

		foreach (var row in serialized)
		{
			await using var cmd = Connection.CreateCommand();
			cmd.Transaction = trans;
			cmd.CommandText = """
				INSERT INTO auction_events (insertion_time, event_type, payload)
				VALUES (@insertion_time, @event_type, @payload::jsonb)
				""";
			cmd.Parameters.Add(new NpgsqlParameter("insertion_time", NpgsqlDbType.TimestampTz) { Value = row.InsertionTime });
			cmd.Parameters.Add(new NpgsqlParameter("event_type", NpgsqlDbType.Text) { Value = row.EventType });
			cmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = row.Payload.RootElement.GetRawText() });
			await cmd.ExecuteNonQueryAsync(ct);
		}

		await trans.CommitAsync(ct);
	}

	private static (string Sql, List<NpgsqlParameter> Parameters) BuildBoundaryPredicate(EventSelector[] boundary)
	{
		if (boundary.Length == 0)
			return ("FALSE", []);

		var disjuncts = new List<string>();
		var parameters = new List<NpgsqlParameter>();

		for (var i = 0; i < boundary.Length; i++)
		{
			var selector = boundary[i];
			var typeParam = $"@t{i}";
			parameters.Add(new NpgsqlParameter(typeParam, NpgsqlDbType.Array | NpgsqlDbType.Text)
			{
				Value = selector.EventTypes.ToArray(),
			});

			var parts = new List<string> { $"event_type = ANY({typeParam})" };
			var ci = 0;
			foreach (var constraint in selector.Constraints)
			{
				var p = $"@p{i}_{ci++}";
				parameters.Add(new NpgsqlParameter(p, NpgsqlDbType.Text) { Value = constraint.Value });
				parts.Add($"payload->>{p} = {p}");
			}
			disjuncts.Add($"({string.Join(" AND ", parts)})");
		}

		return (string.Join(" OR ", disjuncts), parameters);
	}

	private static void ValidatePayload(AppEvent row)
	{
		var root = row.Payload.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
			throw new InvalidOperationException($"Event {row.EventType} must serialize to a JSON object.");

		if (ScalarDimensionCount(root) == 0)
			throw new InvalidOperationException($"Event {row.EventType} must carry at least one top-level scalar property.");
	}

	private static int ScalarDimensionCount(JsonElement root)
	{
		var count = 0;
		foreach (var property in root.EnumerateObject())
		{
			if (IsScalar(property.Value))
				count++;
		}
		return count;
	}

	private static IEnumerable<(string Property, string Value)> ExtractScalarDimensions(JsonElement root)
	{
		foreach (var property in root.EnumerateObject())
		{
			if (IsScalar(property.Value))
				yield return (property.Name, ScalarText(property.Value));
		}
	}

	private static bool IsScalar(JsonElement element) => element.ValueKind
		is JsonValueKind.String
		or JsonValueKind.Number
		or JsonValueKind.True
		or JsonValueKind.False;

	private static string ScalarText(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.String => element.GetString() ?? "",
		_ => element.GetRawText(),
	};

	private static long StableHash(string property, string value)
	{
		const ulong offset = 14695981039346656037UL;
		const ulong prime = 1099511628211UL;
		var hash = offset;
		foreach (var b in Encoding.UTF8.GetBytes(property + "\u001f" + value))
		{
			hash ^= b;
			hash *= prime;
		}
		return unchecked((long)hash);
	}
}
