using Npgsql;

namespace appcore.Infra;

public static class EventStoreSchema
{
	public const string TableName = "app_events";

	public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken ct = default)
	{
		await using var cmd = dataSource.CreateCommand($"""
			CREATE TABLE IF NOT EXISTS {TableName} (
				sequence_id   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
				insertion_time timestamptz NOT NULL DEFAULT now(),
				event_type     text NOT NULL,
				payload        jsonb NOT NULL
			)
			""");
		await cmd.ExecuteNonQueryAsync(ct);
	}
}
