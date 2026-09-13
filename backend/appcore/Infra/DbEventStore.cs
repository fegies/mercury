using System.Buffers.Binary;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using appcore.Entities;
using appcore.Infra.Events;
using Npgsql;
using NpgsqlTypes;

namespace appcore.Infra;

public class DbEventStore(NpgsqlDataSource _datasource, IAppBus? _bus = null) : IEventStore, IEventReader
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

		return new EventContext(boundary, rows, head);
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
			.Select(kv => StableHash(kv.Property, kv.Value))
			.Distinct()
			.Order()
			.ToList();

		await using var conn = _datasource.CreateConnection();
		await conn.OpenAsync(ct);
		// await using var trans = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);

		// Single roundtrip: acquire all advisory locks, then re-check the scope as the last batch command.
		// Locks precede the check so overlapping appends serialize before the scope is re-evaluated (write-skew prevention under READ COMMITTED).
		var (predicate, parameters) = BuildBoundaryPredicate(boundary.Selectors);
		await using var batch = new NpgsqlBatch(conn);

		batch.BatchCommands.Add(new NpgsqlBatchCommand("BEGIN"));


		var lockCmd = new NpgsqlBatchCommand("SELECT count(pg_advisory_xact_lock(l)) from unnest(@keys) l");
		lockCmd.Parameters.Add(new NpgsqlParameter("keys", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = lockKeys });
		batch.BatchCommands.Add(lockCmd);

		var checkCmd = new NpgsqlBatchCommand($"""
				SELECT assert_true(NOT EXISTS (
					SELECT 1 FROM app_events
					WHERE sequence_id > @head AND ({predicate})
				), 'mercury_err_concurrency_conflict')
				""");
		checkCmd.Parameters.Add(new NpgsqlParameter("head", NpgsqlDbType.Bigint) { Value = boundary.LastPosition });
		foreach (var parameter in parameters)
			checkCmd.Parameters.Add(parameter);
		batch.BatchCommands.Add(checkCmd);

		// same roundtrip: insert all result events. insertion_time is assigned by the database (DEFAULT now()).

		{
			var rows_params = serialized.Select((tv, i) => new NpgsqlParameter[] {
				 new($"ty_{i}", NpgsqlDbType.Text) { Value = tv.EventType },
				 new($"pl_{i}", NpgsqlDbType.Jsonb) { Value = tv.Payload.RootElement.GetRawText() }
			}).ToList();

			var vals_clause = string.Join(',', rows_params.Select(r => $"(@{r[0].ParameterName}, @{r[1].ParameterName})"));

			var insert_cmd = new NpgsqlBatchCommand($"""
				INSERT INTO app_events (event_type, payload)
				VALUES {vals_clause}
			""");

			foreach (var r in rows_params)
				foreach (var p in r)
					insert_cmd.Parameters.Add(p);

			batch.BatchCommands.Add(insert_cmd);
		}


		batch.BatchCommands.Add(new NpgsqlBatchCommand("COMMIT"));

		await batch.ExecuteNonQueryAsync(ct);

		// The commit succeeded (the batch is BEGIN..COMMIT in one roundtrip); wake any in-memory
		// consumers with the typed events so they can react without polling the durable log again.
		if (_bus is not null)
			foreach (var domainEvent in events)
				await _bus.EmitAsync(domainEvent, ct);
	}

	private static (string Sql, List<NpgsqlParameter> Parameters) BuildBoundaryPredicate(IReadOnlyList<EventSelector> boundary)
	{
		if (boundary.Count == 0)
			return ("FALSE", []);

		var disjuncts = new List<string>();
		var parameters = new List<NpgsqlParameter>();

		for (var i = 0; i < boundary.Count; i++)
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
		var hsh = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
		hsh.AppendData(MemoryMarshal.Cast<char, byte>(property));
		hsh.AppendData("\u001f"u8);
		hsh.AppendData(MemoryMarshal.Cast<char, byte>(value));

		Span<byte> dst = stackalloc byte[SHA1.HashSizeInBytes];
		hsh.GetCurrentHash(dst);
		return BinaryPrimitives.ReadInt64LittleEndian(dst);
	}
}
