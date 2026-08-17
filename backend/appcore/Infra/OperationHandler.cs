using System.Text.Json;
using appcore.Data;
using appcore.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace appcore.Infra;

public interface IContextProvider
{
	Task<List<AppEvent>> QueryEvents(string? eventType = null);
	Task AssertConsistency();
}

public class DbEventStore : IContextProvider
{
	internal readonly NpgsqlConnection Connection;
	private long _max_observed_sequenceid = long.MinValue;

	public DbEventStore(ApplicationDbContext context)
	{
		Connection = context.Database.GetDbConnection() as NpgsqlConnection
			?? throw new InvalidOperationException("Expected NpgsqlConnection");
	}

	public async Task<List<AppEvent>> QueryEvents(string? eventType = null)
	{
		await using var cmd = Connection.CreateCommand();
		if (eventType is not null)
		{
			cmd.CommandText = """
				SELECT sequence_id, insertion_time, event_type, payload
				FROM auction_events
				WHERE event_type = @type
				ORDER BY sequence_id
				""";
			cmd.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = eventType });
		}
		else
		{
			cmd.CommandText = """
				SELECT sequence_id, insertion_time, event_type, payload
				FROM auction_events
				ORDER BY sequence_id
				""";
		}

		var rows = new List<AppEvent>();
		await using var reader = await cmd.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			rows.Add(new AppEvent
			{
				SequenceId = reader.GetInt64(0),
				InsertionTime = reader.GetDateTime(1),
				EventType = reader.GetString(2),
				Payload = JsonDocument.Parse(reader.GetString(3)),
			});
		}

		if (rows.Count > 0)
		{
			var m = rows.Max(r => r.SequenceId);
			if (m > _max_observed_sequenceid)
				_max_observed_sequenceid = m;
		}

		return rows;
	}

	public async Task AssertConsistency()
	{
		await using var cmd = Connection.CreateCommand();
		cmd.CommandText = "SELECT COALESCE(MAX(sequence_id), 0) FROM auction_events";
		var result = await cmd.ExecuteScalarAsync();
		var currentMax = (long)(result ?? 0);

		if (currentMax > _max_observed_sequenceid)
			throw new ContextInconsistentException();
	}
}

public class ContextInconsistentException() : Exception { }

public record EvaluatorResult<TResult>(TResult Value, List<StoredEvent> GeneratedEvents);

public interface EventEvaluator<TEvent, TResult>
{
	public Task<EvaluatorResult<TResult>> EvaluateEventWithContext(TEvent input, CancellationToken ct);
}

public class IncomingEventHandler<TEvent, TResult>(DbEventStore store, EventEvaluator<TEvent, TResult> evaluator)
{
	public async Task Execute(TEvent input, CancellationToken ct)
	{
		const int maxRetries = 10;
		for (var attempt = 0; attempt < maxRetries; attempt++)
		{
			try
			{
				ct.ThrowIfCancellationRequested();
				await TryExecuteAndPersist(input, ct);
				return;
			}
			catch (NpgsqlException) when (attempt < maxRetries - 1)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt)), ct);
			}
		}
	}

	private async Task<TResult> TryExecuteAndPersist(TEvent input, CancellationToken ct)
	{
		var result = await evaluator.EvaluateEventWithContext(input, ct);

		if (result.GeneratedEvents.Count > 0)
		{
			await store.Connection.OpenAsync(ct);
			using var trans = await store.Connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
			await store.AssertConsistency();

			foreach (var domainEvent in result.GeneratedEvents)
			{
				var row = EventSerializer.Serialize(domainEvent);
				await using var cmd = store.Connection.CreateCommand();
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
		return result.Value;
	}
}
