using System.Collections.Concurrent;
using System.Threading.Channels;

namespace appcore.Infra.Live;

/// <summary>
/// Registry of open live-stream connections. Pure fan-out primitive with no
/// bus or domain knowledge: the bridge resolves domain events into messages
/// and pushes them here; the SSE controller registers/unregisters connections.
/// Supports several concurrent connections per user (multiple tabs).
/// </summary>
/// <remarks>
/// Per-connection queues are bounded channels in <see cref="BoundedChannelFullMode.DropOldest"/>
/// mode: a stalled client must never block the bus pump, and a dropped message
/// is acceptable since live events are transient by design.
/// </remarks>
public sealed class LiveEventHub
{
	private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<long, Channel<LiveEvent>>> _byUser = new();
	private long _nextConnectionId;
	private long _openConnections;

	/// <summary>True while at least one connection is open.</summary>
	public bool HasConnections => Volatile.Read(ref _openConnections) > 0;

	/// <summary>
	/// Opens a connection for the user. Dispose the returned connection when
	/// its request ends; disposal is idempotent.
	/// </summary>
	public LiveConnection Connect(Guid userId)
	{
		var channel = Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(capacity: 64)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
			SingleWriter = false,
		});

		var id = Interlocked.Increment(ref _nextConnectionId);
		var connections = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<long, Channel<LiveEvent>>());
		connections[id] = channel;
		Interlocked.Increment(ref _openConnections);

		return new LiveConnection(channel.Reader, () => Disconnect(userId, id, connections, channel));
	}

	private void Disconnect(Guid userId, long id, ConcurrentDictionary<long, Channel<LiveEvent>> connections, Channel<LiveEvent> channel)
	{
		if (!connections.TryRemove(id, out _))
			return;
		channel.Writer.TryComplete();
		Interlocked.Decrement(ref _openConnections);
		if (connections.IsEmpty)
			((ICollection<KeyValuePair<Guid, ConcurrentDictionary<long, Channel<LiveEvent>>>>)_byUser)
				.Remove(new(userId, connections));
	}

	/// <summary>Pushes a message to every open connection of the user.</summary>
	public void PushToUser(Guid userId, LiveEvent message)
	{
		if (!_byUser.TryGetValue(userId, out var connections))
			return;
		foreach (var channel in connections.Values)
			channel.Writer.TryWrite(message);
	}

	/// <summary>Pushes a message to every open connection of every user.</summary>
	public void Broadcast(LiveEvent message)
	{
		foreach (var connections in _byUser.Values)
			foreach (var channel in connections.Values)
				channel.Writer.TryWrite(message);
	}
}

/// <summary>
/// One open live-stream connection. Exposes the read side the SSE loop drains;
/// dispose when the request ends (idempotent, completes the channel so the
/// reader loop drains buffered items and then terminates).
/// </summary>
public sealed class LiveConnection(ChannelReader<LiveEvent> reader, Action disconnect) : IDisposable
{
	private Action? _disconnect = disconnect;

	public ChannelReader<LiveEvent> Reader { get; } = reader;

	public void Dispose() => Interlocked.Exchange(ref _disconnect, null)?.Invoke();
}
