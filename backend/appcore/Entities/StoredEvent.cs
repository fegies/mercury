using System.Text.Json;

namespace appcore.Entities;

/// <summary>
/// The user entity
/// </summary>
public abstract class StoredEvent
{
    /// <summary>
    /// The event sequence id
    /// </summary>
    public long SequenceId { get; set; }

    /// <summary>
    /// When was this event inserted?
    /// </summary>
    public DateTime InsertionTime { get; set; } = DateTime.UtcNow;
}
