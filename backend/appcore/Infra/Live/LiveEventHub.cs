using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace appcore.Infra.Live;

/// <summary>
/// Registry of open live-stream connections. Pure fan-out primitive with no
/// bus or domain knowledge: the bridge resolves domain events into messages
/// and pushes them here; the SSE controller registers/unregisters connections.
/// Supports several concurrent connections per user (multiple tabs), bounded
/// by <see cref="MaxConnectionsPerUser"/>.
/// </summary>
/// <remarks>
/// The registry is guarded by a single lock (same pattern as the app bus's
/// subscriber registry): connect/disconnect mutate it under the lock, and the
/// empty-entry cleanup of a disconnect can therefore never race a concurrent
/// connect into orphaning a fresh connection. Pushes enumerate under the lock
/// but only perform non-blocking channel writes, so a stalled client can
/// never hold the lock — bounded drop-oldest channels absorb backpressure.
/// </remarks>
public sealed class LiveEventHub
{
	/// <summary>
	/// Ceiling on simultaneous connections per user. Tabs are legitimate;
	/// thousands of open streams per identity are not — the SSE endpoint
	/// surfaces the refusal as HTTP 429.
	/// </summary>
	private const int MaxConnectionsPerUser = 5;

	private readonly Lock _registryLock = new();
	private readonly Dictionary<Guid, Dictionary<long, Channel<LiveEvent>>> _byUser = [];
	private long _nextConnectionId;
	private long _openConnections;

	/// <summary>True while at least one connection is open.</summary>
	public bool HasConnections => Volatile.Read(ref _openConnections) > 0;

	/// <summary>
	/// Opens a connection for the user unless they already hold
	/// <see cref="MaxConnectionsPerUser"/> open connections. Dispose the
	/// returned connection when its request ends; disposal is idempotent.
	/// </summary>
	public bool TryConnect(Guid userId, [NotNullWhen(true)] out LiveConnection? connection)
	{
		connection = null;
		lock (_registryLock)
		{
			if (!_byUser.TryGetValue(userId, out var connections))
				connections = _byUser[userId] = [];

			if (connections.Count >= MaxConnectionsPerUser)
				return false;

			var id = _nextConnectionId++;
			var channel = Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(capacity: 64)
			{
				FullMode = BoundedChannelFullMode.DropOldest,
				SingleReader = true,
				SingleWriter = false,
			});
			connections[id] = channel;
			Interlocked.Increment(ref _openConnections);

			connection = new LiveConnection(channel.Reader, () => Disconnect(userId, id, channel));
			return true;
		}
	}

	private void Disconnect(Guid userId, long id, Channel<LiveEvent> channel)
	{
		lock (_registryLock)
		{
			if (!_byUser.TryGetValue(userId, out var connections) || !connections.Remove(id))
				return;
			channel.Writer.TryComplete();
			Interlocked.Decrement(ref _openConnections);
			if (connections.Count == 0)
				_byUser.Remove(userId);
		}
	}

	/// <summary>Pushes a message to every open connection of the user.</summary>
	public void PushToUser(Guid userId, LiveEvent message)
	{
		lock (_registryLock)
		{
			if (!_byUser.TryGetValue(userId, out var connections))
				return;
			foreach (var channel in connections.Values)
				channel.Writer.TryWrite(message);
		}
	}

	/// <summary>Pushes a message to every open connection of every user.</summary>
	public void Broadcast(LiveEvent message)
	{
		lock (_registryLock)
		{
			foreach (var connections in _byUser.Values)
				foreach (var channel in connections.Values)
					channel.Writer.TryWrite(message);
		}
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
