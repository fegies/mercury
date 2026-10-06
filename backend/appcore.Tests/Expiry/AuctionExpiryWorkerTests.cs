using appcore.Configuration;
using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Events;
using appcore.Infra.Evaluators;
using appcore.Infra.Expiry;
using appcore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace appcore.Tests.Expiry;

public class AuctionExpiryWorkerTests
{
	private readonly FakeTimeProvider _clock = new();
	private readonly InMemoryEventStore _store = new();
	private readonly InMemoryAppBus _bus = new(NullLogger<InMemoryAppBus>.Instance);
	private readonly IServiceScopeFactory _scopeFactory;

	public AuctionExpiryWorkerTests()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IEventStore>(_store);
		services.AddSingleton<IEventReader>(_store);
		services.AddSingleton(new AuctionConfig());
		services.AddSingleton(new EventHandlerOptions());
		services.AddSingleton<IDecisionFunction<AuctionClosed, bool>, CloseAuctionEvaluator>(sp =>
			new CloseAuctionEvaluator(sp.GetRequiredService<AuctionConfig>()));
		services.AddSingleton<IncomingEventHandler<AuctionClosed, bool>>();
		_scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
	}

	[Fact]
	public async Task ExpiresAnAuctionWithWinningBidderAndPrice()
	{
		var auctionId = Guid.NewGuid();
		var bidderId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(2);

		await using var host = await StartAsync(
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline },
			 new BidPlaced { AuctionId = auctionId, BidderId = bidderId, MaximumAmount = 100m }]);

		_clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));
		var closed = await host.WaitForClosed(auctionId);
		Assert.NotNull(closed);

		Assert.Equal(AuctionCloseReason.Expired, closed.Reason);
		Assert.Equal(bidderId, closed.WinnerUserId);
		Assert.Equal(10m, closed.WinningPrice);
	}

	[Fact]
	public async Task ExpiresAnAuctionWithoutWinner()
	{
		var auctionId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(1);

		await using var host = await StartAsync(
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline }]);

		_clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
		var closed = await host.WaitForClosed(auctionId);
		Assert.NotNull(closed);

		Assert.Equal(AuctionCloseReason.Expired, closed.Reason);
		Assert.Null(closed.WinnerUserId);
		Assert.Null(closed.WinningPrice);
	}

	[Fact]
	public async Task ManualCloseBeforeDeadlinePreventsAutoClose()
	{
		var auctionId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(2);

		var host = await StartAsync(
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline }]);
		await _bus.EmitAsync(new AuctionClosed { AuctionId = auctionId, Reason = AuctionCloseReason.Manual });
		await Settle();

		_clock.Advance(TimeSpan.FromHours(3));
		Assert.Null(await host.WaitForClosed(auctionId, timeout: 300));
	}

	[Fact]
	public async Task CancelBeforeDeadlinePreventsAutoClose()
	{
		var auctionId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(2);

		var host = await StartAsync(
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline }]);
		await _bus.EmitAsync(new AuctionCancelled { AuctionId = auctionId });
		await Settle();

		_clock.Advance(TimeSpan.FromHours(3));
		Assert.Null(await host.WaitForClosed(auctionId, timeout: 300));
	}

	[Fact]
	public async Task ExtendingClosureTimeMovesTheDeadline()
	{
		var auctionId = Guid.NewGuid();
		var first = _clock.GetUtcNow().UtcDateTime.AddHours(1);
		var second = _clock.GetUtcNow().UtcDateTime.AddHours(4);

		var host = await StartAsync(
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = first }]);
		await _bus.EmitAsync(new AuctionCloseExtended { AuctionId = auctionId, NewClosureTime = second });
		await Settle();

		_clock.Advance(TimeSpan.FromHours(2));
		Assert.Null(await host.WaitForClosed(auctionId, timeout: 300));
		_clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

		var closed = await host.WaitForClosed(auctionId);
		Assert.NotNull(closed);
		Assert.Equal(AuctionCloseReason.Expired, closed.Reason);
	}

	[Fact]
	public async Task StartupReconciliationArmsOpenAuctionsFromPreexistingEvents()
	{
		var auctionId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(2);
		await Append(new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline });
		await Append(new BidPlaced { AuctionId = auctionId, BidderId = Guid.NewGuid(), MaximumAmount = 50m });

		await using var host = await StartAsync([]);
		_clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

		var closed = await host.WaitForClosed(auctionId);
		Assert.NotNull(closed);
		Assert.Equal(AuctionCloseReason.Expired, closed.Reason);
	}

	[Fact]
	public async Task StartupReconciliationSkipsAlreadyClosedAuctions()
	{
		var auctionId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(1);
		await Append(new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline });
		await Append(new AuctionClosed { AuctionId = auctionId, Reason = AuctionCloseReason.Manual });

		await using var host = await StartAsync([]);
		_clock.Advance(TimeSpan.FromHours(3));

		Assert.Null(await host.WaitForClosed(auctionId, timeout: 300));
	}

	[Fact]
	public async Task ExpiryAfterManualCloseIsSwallowed()
	{
		var auctionId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(2);

		var host = await StartAsync(
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline }]);
		await Append(new AuctionClosed { AuctionId = auctionId, Reason = AuctionCloseReason.Manual });
		await _bus.EmitAsync(new AuctionExpiryElapsed(auctionId, deadline));
		await Settle();

		Assert.Null(await host.WaitForClosed(auctionId, timeout: 200));
	}

	[Fact]
	public async Task ExhaustedRetriesReArmTheDeadlineAndCloseLater()
	{
		var auctionId = Guid.NewGuid();
		var bidderId = Guid.NewGuid();
		var deadline = _clock.GetUtcNow().UtcDateTime.AddHours(1);

		var store = new FlakyAppendStore();
		await AppendTo(store,
			[new AuctionCreated { AuctionId = auctionId, Title = "Widget", MinimumPrice = 10m, ClosureTime = deadline },
			 new BidPlaced { AuctionId = auctionId, BidderId = bidderId, MaximumAmount = 100m }]);

		var services = new ServiceCollection();
		services.AddSingleton<IEventStore>(store);
		services.AddSingleton<IEventReader>(store);
		services.AddSingleton(new AuctionConfig());
		services.AddSingleton(new EventHandlerOptions());
		services.AddSingleton<IDecisionFunction<AuctionClosed, bool>, CloseAuctionEvaluator>(sp =>
			new CloseAuctionEvaluator(sp.GetRequiredService<AuctionConfig>()));
		services.AddSingleton<IncomingEventHandler<AuctionClosed, bool>>();
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		await using var host = new WorkerHost(_bus, store.Events,
			new AuctionExpiryWorker(_bus, scopeFactory, _clock, NullLogger<AuctionExpiryWorker>.Instance));
		await host.Start();

		// Exactly one close cycle's worth of conflicts: all retries lose, the close
		// exhausts into ConvergenceException, and the worker must re-arm the deadline.
		store.FailuresRemaining = 10;

		_clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
		Assert.Null(await host.WaitForClosed(auctionId, timeout: 2500));

		_clock.Advance(TimeSpan.FromSeconds(11));
		var closed = await host.WaitForClosed(auctionId, timeout: 2500);
		Assert.NotNull(closed);
		Assert.Equal(AuctionCloseReason.Expired, closed.Reason);
		Assert.Equal(bidderId, closed.WinnerUserId);
	}

	private async Task<WorkerHost> StartAsync(StoredEvent[] seed)
	{
		await Append(seed);
		var host = new WorkerHost(_bus, _store.Events,
			new AuctionExpiryWorker(_bus, _scopeFactory, _clock, NullLogger<AuctionExpiryWorker>.Instance));
		await host.Start();
		return host;
	}

	private Task Settle() => Task.Delay(100);

	private Task Append(params StoredEvent[] events)
	{
		foreach (var (row, i) in events.Select(e => EventSerializer.Serialize(e)).Select((r, i) => (r, i)))
			_store.Seed([row with { SequenceId = _store.Events.Count + i + 1 }]);
		return Task.CompletedTask;
	}

	private async Task AppendTo(FlakyAppendStore store, StoredEvent[] events)
	{
		foreach (var (row, i) in events.Select(e => EventSerializer.Serialize(e)).Select((r, i) => (r, i)))
			store.Seed([row with { SequenceId = store.Events.Count + i + 1 }]);
		await Task.CompletedTask;
	}

	private sealed class WorkerHost : IAsyncDisposable
	{
		private readonly InMemoryAppBus _bus;
		private readonly IReadOnlyList<AppEvent> _events;
		private readonly AuctionExpiryWorker _worker;

		public WorkerHost(InMemoryAppBus bus, IReadOnlyList<AppEvent> events, AuctionExpiryWorker worker)
		{
			_bus = bus;
			_events = events;
			_worker = worker;
		}

		public async Task Start()
		{
			await _bus.StartAsync(CancellationToken.None);
			await _worker.StartAsync(CancellationToken.None);
		}

		public async Task<AuctionClosed?> WaitForClosed(Guid auctionId, int timeout = 5000)
		{
			var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
			while (DateTime.UtcNow < deadline)
			{
				var closed = _events
					.Select(EventSerializer.Deserialize)
					.OfType<AuctionClosed>()
					.LastOrDefault(c => c.AuctionId == auctionId && c.Reason == AuctionCloseReason.Expired);
				if (closed is not null)
					return closed;
				await Task.Delay(10);
			}
			return null;
		}

		public async ValueTask DisposeAsync()
		{
			await _worker.StopAsync(CancellationToken.None);
			await _bus.StopAsync(CancellationToken.None);
		}
	}

	/// <summary>
	/// Delegating store whose appends throw conflicts for the first
	/// <see cref="FailuresRemaining"/> attempts, to exercise the retry and
	/// re-arm paths against a deadline-triggered close.
	/// </summary>
	private sealed class FlakyAppendStore : IEventStore, IEventReader
	{
		private readonly InMemoryEventStore _inner = new();

		public int FailuresRemaining { get; set; }

		public IEventReader Reader => this;

		public IReadOnlyList<AppEvent> Events => _inner.Events;

		public void Seed(IEnumerable<AppEvent> events) => _inner.Seed(events);

		public Task<EventContext> Read(EventSelector[] boundary, CancellationToken ct)
			=> _inner.Read(boundary, ct);

		public async Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
		{
			if (FailuresRemaining > 0)
			{
				FailuresRemaining--;
				throw new ConcurrencyConflictException();
			}
			await _inner.Append(events, boundary, ct);
		}
	}
}