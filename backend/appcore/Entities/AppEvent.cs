using System.Text.Json;

namespace appcore.Entities;

public record AppEvent
{
	public long SequenceId { get; init; }
	public DateTime InsertionTime { get; init; } = DateTime.UtcNow;
	public required string EventType { get; init; }
	public required JsonDocument Payload { get; init; }
}
