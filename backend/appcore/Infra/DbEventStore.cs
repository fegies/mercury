using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using appcore.Entities;
using Npgsql;
using NpgsqlTypes;

namespace appcore.Infra;

public class DbEventStore(NpgsqlDataSource _datasource) : IEventStore, IEventReader
{

	public IEventReader Reader => this;

	public async Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct)
	{
		var (predicate, parameters) = BuildBoundaryPredicate(boundary);

		await using var cmd = _datasource.CreateCommand();
		cmd.CommandText = $"""
			SELECT sequence_id, insertion_time, event_type, payload
			FROM app_events
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
			var sequenceId = reader.GetInt64(0);
			rows.Add(new AppEvent
			{
				SequenceId = sequenceId,
				InsertionTime = reader.GetDateTime(1),
				EventType = reader.GetString(2),
				Payload = JsonDocument.Parse(reader.GetString(3)),
			});
			if (sequenceId > head)
				head = sequenceId;
		}

		return new EventContext(rows, head);
	}

	public async Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
	{
		if (events.Count == 0)
			return;

		var serialized = new List<AppEvent>(events.Count);
		var writeKeys = new List<(string Property, string Value)>();
		foreach (var domainEvent in events)
		{
			var row = EventSerializer.Serialize(domainEvent);
			EventPayload.Validate(row);
			writeKeys.AddRange(EventPayload.ExtractScalarDimensions(row.Payload.RootElement));
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

		await using var conn = _datasource.CreateConnection();
		await conn.OpenAsync(ct);
		await using var trans = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

		{
			// Single roundtrip: acquire all advisory locks, then re-check the scope as the last batch command.
			// Locks precede the check so overlapping appends serialize before the scope is re-evaluated (write-skew prevention under READ COMMITTED).
			var (predicate, parameters) = BuildBoundaryPredicate(boundary.Selectors.ToArray());
			await using var batch = new NpgsqlBatch(conn, trans);

			foreach (var (property, value) in lockKeys)
			{
				var lockCmd = new NpgsqlBatchCommand("SELECT pg_advisory_xact_lock(@key)");
				lockCmd.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bigint) { Value = StableHash(property, value) });
				batch.BatchCommands.Add(lockCmd);
			}
			var checkCmd = new NpgsqlBatchCommand($"""
			SELECT EXISTS (
				SELECT 1 FROM app_events
				WHERE sequence_id > @head AND ({predicate})
			)
			""");
			checkCmd.Parameters.Add(new NpgsqlParameter("head", NpgsqlDbType.Bigint) { Value = boundary.LastPosition });
			foreach (var parameter in parameters)
				checkCmd.Parameters.Add(parameter);
			batch.BatchCommands.Add(checkCmd);

			await using var reader = await batch.ExecuteReaderAsync(ct);
			for (var i = 0; i < lockKeys.Count; i++)
			{
				while (await reader.ReadAsync(ct)) { }
				await reader.NextResultAsync(ct);
			}
			var conflicted = await reader.ReadAsync(ct) && reader.GetBoolean(0);
			if (conflicted)
				throw new ConcurrencyConflictException();
		}

		{
			// Single roundtrip: insert all result events. insertion_time is assigned by the database (DEFAULT now()).
			await using var insertBatch = new NpgsqlBatch(conn, trans);

			foreach (var row in serialized)
			{
				var insertCmd = new NpgsqlBatchCommand("""
				INSERT INTO app_events (event_type, payload)
				VALUES (@event_type, @payload::jsonb)
				""");
				insertCmd.Parameters.Add(new NpgsqlParameter("event_type", NpgsqlDbType.Text) { Value = row.EventType });
				insertCmd.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = row.Payload.RootElement.GetRawText() });
				insertBatch.BatchCommands.Add(insertCmd);
			}
			await insertBatch.ExecuteNonQueryAsync(ct);
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
				var pn = p + "_n";
				var pv = p + "_v";
				parameters.Add(new NpgsqlParameter(pn, NpgsqlDbType.Text) { Value = constraint.Property });
				parameters.Add(new NpgsqlParameter(pv, constraint.Value));
				parts.Add($"payload->>{pn} = {pv}");
			}
			disjuncts.Add($"({string.Join(" AND ", parts)})");
		}

		return (string.Join(" OR ", disjuncts), parameters);
	}

	private static long StableHash(string property, string value)
	{
		var data = Encoding.UTF8.GetBytes(property + "\u001f" + value);
		return BitConverter.ToInt64(SHA1.HashData(data), 0);
	}
}
