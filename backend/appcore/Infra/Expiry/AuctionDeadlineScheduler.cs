using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace appcore.Infra.Expiry;

/// <summary>
/// Single-deadline timer for open auctions. Tracks one deadline per auction id and
/// surfaces the next due deadline as an <see cref="AuctionExpiryElapsed"/> stream.
/// Safe for far-future deadlines: the delay is capped so the clock is never parked
/// further ahead than <see cref="DelayCap"/>, and every <see cref="Schedule"/> /
/// <see cref="Remove"/> wakes a parked wait so newly registered deadlines are never
/// starved behind the cap. Stale heap entries left by deadline updates are skipped on
/// the next pass. Loss-tolerant by design — this is a wakeup index, not the source of truth.
/// </summary>
public sealed class AuctionDeadlineScheduler(TimeProvider timeProvider)
{
	public static readonly TimeSpan DelayCap = TimeSpan.FromHours(24);

	private readonly Lock _lock = new();
	private readonly Dictionary<Guid, DateTime> _deadlines = [];
	private readonly PriorityQueue<(Guid AuctionId, DateTime Deadline), DateTime> _heap = new();
	private readonly Channel<bool> _changed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
	{
		FullMode = BoundedChannelFullMode.DropWrite,
		SingleReader = true,
	});

	public int Count
	{
		get { lock (_lock) return _deadlines.Count; }
	}

	/// <summary>Register or update the deadline for an auction. Replaces any previous one.</summary>
	public void Schedule(Guid auctionId, DateTime deadline)
	{
		lock (_lock)
		{
			_deadlines[auctionId] = deadline;
			_heap.Enqueue((auctionId, deadline), deadline);
		}
		_changed.Writer.TryWrite(true);
	}

	/// <summary>Drop all pending deadlines for an auction (e.g. it was closed or cancelled).</summary>
	public void Remove(Guid auctionId)
	{
		lock (_lock)
			_deadlines.Remove(auctionId);
		_changed.Writer.TryWrite(true);
	}

	/// <summary>
	/// Yields each auction as its deadline arrives, in chronological order, then waits a
	/// bounded delay until the next due deadline (or until the schedule changes). Cancellation
	/// stops the iteration.
	/// </summary>
	public async IAsyncEnumerable<AuctionExpiryElapsed> ElapsedAsync(
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		while (!ct.IsCancellationRequested)
		{
			var due = TryGetDue();
			if (due is not null)
			{
				yield return due;
				continue;
			}

			try
			{
				await WaitUntilNextCheckAsync(NextDelay(), ct);
			}
			catch (OperationCanceledException)
			{
				yield break;
			}
		}
	}

	private async Task WaitUntilNextCheckAsync(TimeSpan delay, CancellationToken ct)
	{
		using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		var timer = Task.Delay(delay, timeProvider, cts.Token);
		var wake = _changed.Reader.WaitToReadAsync(cts.Token).AsTask();
		var winner = await Task.WhenAny(timer, wake);
		cts.Cancel();
		if (winner == wake)
			_changed.Reader.TryRead(out _);
		ct.ThrowIfCancellationRequested();
	}

	private AuctionExpiryElapsed? TryGetDue()
	{
		var now = timeProvider.GetUtcNow().UtcDateTime;

		lock (_lock)
		{
			while (_heap.Count > 0)
			{
				var (auctionId, deadline) = _heap.Peek();
				if (_deadlines.TryGetValue(auctionId, out var current) && current == deadline)
				{
					if (deadline <= now)
					{
						_heap.Dequeue();
						_deadlines.Remove(auctionId);
						return new AuctionExpiryElapsed(auctionId, deadline);
					}
					return null;
				}
				_heap.Dequeue();
			}
			return null;
		}
	}

	private TimeSpan NextDelay()
	{
		var now = timeProvider.GetUtcNow().UtcDateTime;

		lock (_lock)
		{
			while (_heap.Count > 0)
			{
				var (auctionId, deadline) = _heap.Peek();
				if (_deadlines.TryGetValue(auctionId, out var current) && current == deadline)
				{
					var untilDeadline = deadline - now;
					if (untilDeadline < TimeSpan.Zero)
						return TimeSpan.Zero;
					return untilDeadline > DelayCap ? DelayCap : untilDeadline;
				}
				_heap.Dequeue();
			}
			return DelayCap;
		}
	}
}