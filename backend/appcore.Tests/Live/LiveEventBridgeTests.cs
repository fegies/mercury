using System.Threading.Channels;
using appcore.Configuration;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Events;
using appcore.Infra.Evaluators;
using appcore.Infra.Live;
using appcore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace appcore.Tests.Live;

/// <summary>
/// Full production path: the store publishes committed events to the bus, the
/// bridge reconstructs the facts it needs and fans messages out through the
/// hub to the open per-user connections.
/// </summary>
public class LiveEventBridgeTests
{
	private sealed class Harness : IAsyncDisposable
	{
		public required InMemoryEventStore Store { get; init; }
		public required InMemoryAppBus Bus { get; init; }
		public required LiveEventHub Hub { get; init; }
		public required ServiceProvider Provider { get; init; }
		public required LiveEventBridge Bridge { get; init; }

		public static async Task<Harness> Start()
		{
			var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
			var store = new InMemoryEventStore(bus: bus);
			var hub = new LiveEventHub();
			var provider = new ServiceCollection()
				.AddSingleton<IEventStore>(store)
				.AddSingleton<IEventReader>(store)
				.AddSingleton<IAppBus>(bus)
				.AddSingleton(new AuctionConfig())
				.AddSingleton(new EventHandlerOptions())
				.AddSingleton(hub)
				.AddSingleton<IDecisionFunction<AuctionCreated, Guid>, CreateAuctionEvaluator>()
				.AddSingleton<IDecisionFunction<BidPlaced, BidResult>, PlaceBidEvaluator>(sp =>
					new PlaceBidEvaluator(sp.GetRequiredService<AuctionConfig>().MinBidIncrement))
				.AddSingleton<IDecisionFunction<AuctionClosed, bool>, CloseAuctionEvaluator>(sp =>
					new CloseAuctionEvaluator(sp.GetRequiredService<AuctionConfig>()))
				.AddSingleton<IDecisionFunction<AuctionCancelled, bool>, CancelAuctionEvaluator>()
				.AddSingleton<IDecisionFunction<AuctionCloseExtended, bool>, ExtendAuctionCloseEvaluator>()
				.AddSingleton<IncomingEventHandler<AuctionCreated, Guid>>()
				.AddSingleton<IncomingEventHandler<BidPlaced, BidResult>>()
				.AddSingleton<IncomingEventHandler<AuctionClosed, bool>>()
				.AddSingleton<IncomingEventHandler<AuctionCancelled, bool>>()
				.AddSingleton<IncomingEventHandler<AuctionCloseExtended, bool>>()
				.BuildServiceProvider();
			var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
			var bridge = new LiveEventBridge(bus, hub, scopeFactory, provider.GetRequiredService<AuctionConfig>());
			await bus.StartAsync(CancellationToken.None);
			await bridge.StartAsync(CancellationToken.None);
			return new Harness { Store = store, Bus = bus, Hub = hub, Provider = provider, Bridge = bridge };
		}

		/// <summary>
		/// Appends the event through its production handler and returns only
		/// after the bus pump finished dispatching it. The pump runs handlers
		/// serially in registration order, and the gate handler is registered
		/// after the bridge's own, so the bridge's handler (and its hub pushes)
		/// are guaranteed complete when this returns.
		/// </summary>
		private async Task DispatchAsync<TMessage>(TMessage message, Func<Task> execute) where TMessage : class
		{
			var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			using var subscription = Bus.Subscribe<TMessage>((_, _) =>
			{
				gate.TrySetResult();
				return ValueTask.CompletedTask;
			});
			await execute();
			await gate.Task;
		}

		public Task CreateAsync(AuctionCreated created) => DispatchAsync(created, async () =>
			await Provider.GetRequiredService<IncomingEventHandler<AuctionCreated, Guid>>().Execute(created, CancellationToken.None));

		public Task BidAsync(BidPlaced bid) => DispatchAsync(bid, async () =>
			await Provider.GetRequiredService<IncomingEventHandler<BidPlaced, BidResult>>().Execute(bid, CancellationToken.None));

		public Task CloseAsync(AuctionClosed closed) => DispatchAsync(closed, async () =>
			await Provider.GetRequiredService<IncomingEventHandler<AuctionClosed, bool>>().Execute(closed, CancellationToken.None));

		public Task CancelAsync(AuctionCancelled cancelled) => DispatchAsync(cancelled, async () =>
			await Provider.GetRequiredService<IncomingEventHandler<AuctionCancelled, bool>>().Execute(cancelled, CancellationToken.None));

		public Task ExtendAsync(AuctionCloseExtended extended) => DispatchAsync(extended, async () =>
			await Provider.GetRequiredService<IncomingEventHandler<AuctionCloseExtended, bool>>().Execute(extended, CancellationToken.None));

		public async ValueTask DisposeAsync()
		{
			await Bridge.StopAsync(CancellationToken.None);
			await Bus.StopAsync(CancellationToken.None);
			await Provider.DisposeAsync();
		}
	}

	private static async Task<LiveEvent> ReadOneAsync(ChannelReader<LiveEvent> reader)
	{
		Assert.True(await reader.WaitToReadAsync());
		Assert.True(reader.TryRead(out var live));
		return live;
	}

	private static async Task<List<LiveEvent>> ReadAllAsync(ChannelReader<LiveEvent> reader, int expected)
	{
		var events = new List<LiveEvent>(expected);
		while (events.Count < expected)
			events.Add(await ReadOneAsync(reader));
		return events;
	}

	/// <summary>
	/// Absence is provable without waiting: after DispatchedAsync returned, the
	/// bridge's synchronous hub pushes for that event are final.
	/// </summary>
	private static void AssertDrained(ChannelReader<LiveEvent> reader)
		=> Assert.False(reader.TryRead(out _));

	[Fact]
	public async Task OverbidNotifiesThePriorLeaderWithTheNewPrice()
	{
		await using var harness = await Harness.Start();
		var auctionId = Guid.NewGuid();
		var alice = Guid.NewGuid();
		var bob = Guid.NewGuid();
		using var aliceConnection = harness.Hub.Connect(alice);
		using var bobConnection = harness.Hub.Connect(bob);

		await harness.CreateAsync(new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "d", MinimumPrice = 10m, ClosureTime = DateTime.UtcNow.AddDays(1) });

		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = alice, MaximumAmount = 100m });

		// Every connected client receives the broadcast ping, not just the bidder.
		var aliceFirst = (await ReadAllAsync(aliceConnection.Reader, 1))[0];
		var bobFirst = (await ReadAllAsync(bobConnection.Reader, 1))[0];
		Assert.Equal(LiveEventKind.AuctionUpdated, aliceFirst.Kind);
		Assert.Equal(LiveEventKind.AuctionUpdated, bobFirst.Kind);

		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = bob, MaximumAmount = 150m });

		var aliceEvents = await ReadAllAsync(aliceConnection.Reader, 2);
		Assert.Equal(LiveEventKind.AuctionUpdated, aliceEvents[0].Kind);
		Assert.Equal(LiveEventType.BidPlaced, aliceEvents[0].Type);
		Assert.Equal(LiveEventKind.Notification, aliceEvents[1].Kind);
		Assert.Equal(LiveEventType.Outbid, aliceEvents[1].Type);
		Assert.Equal(auctionId, aliceEvents[1].AuctionId);
		Assert.Equal("Widget", aliceEvents[1].Title);
		Assert.Equal(100.5m, aliceEvents[1].Price);

		var bobEvents = await ReadAllAsync(bobConnection.Reader, 1);
		Assert.Equal(LiveEventKind.AuctionUpdated, bobEvents[0].Kind);
		AssertDrained(aliceConnection.Reader);
		AssertDrained(bobConnection.Reader);
	}

	[Fact]
	public async Task SelfRaiseKeepsTheLeadAndProducesNoNotification()
	{
		await using var harness = await Harness.Start();
		var auctionId = Guid.NewGuid();
		var alice = Guid.NewGuid();
		using var aliceConnection = harness.Hub.Connect(alice);

		await harness.CreateAsync(new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "d", MinimumPrice = 10m, ClosureTime = DateTime.UtcNow.AddDays(1) });
		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = alice, MaximumAmount = 100m });
		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = alice, MaximumAmount = 150m });

		var events = await ReadAllAsync(aliceConnection.Reader, 2);
		Assert.All(events, e => Assert.Equal(LiveEventKind.AuctionUpdated, e.Kind));
		AssertDrained(aliceConnection.Reader);
	}

	[Fact]
	public async Task CloseNotifiesTheWinnerWithTheWinningPrice()
	{
		await using var harness = await Harness.Start();
		var auctionId = Guid.NewGuid();
		var alice = Guid.NewGuid();
		using var aliceConnection = harness.Hub.Connect(alice);

		await harness.CreateAsync(new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "d", MinimumPrice = 10m, ClosureTime = DateTime.UtcNow.AddDays(1) });
		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = alice, MaximumAmount = 100m });
		await ReadAllAsync(aliceConnection.Reader, 1);

		await harness.CloseAsync(new AuctionClosed { AuctionId = auctionId, Reason = AuctionCloseReason.Manual });

		var events = await ReadAllAsync(aliceConnection.Reader, 2);
		Assert.Equal(LiveEventKind.AuctionUpdated, events[0].Kind);
		Assert.Equal(LiveEventType.Closed, events[0].Type);
		Assert.Equal(LiveEventKind.Notification, events[1].Kind);
		Assert.Equal(LiveEventType.Won, events[1].Type);
		Assert.Equal("Widget", events[1].Title);
		Assert.Equal(10m, events[1].Price);
	}

	[Fact]
	public async Task CancellationNotifiesEveryBidder()
	{
		await using var harness = await Harness.Start();
		var auctionId = Guid.NewGuid();
		var alice = Guid.NewGuid();
		var bob = Guid.NewGuid();
		using var aliceConnection = harness.Hub.Connect(alice);
		using var bobConnection = harness.Hub.Connect(bob);

		await harness.CreateAsync(new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "d", MinimumPrice = 10m, ClosureTime = DateTime.UtcNow.AddDays(1) });

		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = alice, MaximumAmount = 100m });
		await ReadAllAsync(aliceConnection.Reader, 1);
		await ReadAllAsync(bobConnection.Reader, 1);

		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = bob, MaximumAmount = 90m });
		await ReadAllAsync(aliceConnection.Reader, 1);
		await ReadAllAsync(bobConnection.Reader, 1);

		await harness.CancelAsync(new AuctionCancelled { AuctionId = auctionId });

		var aliceEvents = await ReadAllAsync(aliceConnection.Reader, 2);
		var bobEvents = await ReadAllAsync(bobConnection.Reader, 2);
		foreach (var events in new[] { aliceEvents, bobEvents })
		{
			Assert.Equal(LiveEventKind.AuctionUpdated, events[0].Kind);
			Assert.Equal(LiveEventType.Cancelled, events[0].Type);
			Assert.Equal(LiveEventKind.Notification, events[1].Kind);
			Assert.Equal(LiveEventType.Cancelled, events[1].Type);
			Assert.Equal("Widget", events[1].Title);
			Assert.Null(events[1].Price);
		}
		AssertDrained(aliceConnection.Reader);
		AssertDrained(bobConnection.Reader);
	}

	[Fact]
	public async Task CloseExtensionBroadcastsAPingOnly()
	{
		await using var harness = await Harness.Start();
		var auctionId = Guid.NewGuid();
		var alice = Guid.NewGuid();
		using var aliceConnection = harness.Hub.Connect(alice);

		await harness.CreateAsync(new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "d", MinimumPrice = 10m, ClosureTime = DateTime.UtcNow.AddDays(1) });
		await harness.ExtendAsync(new AuctionCloseExtended { AuctionId = auctionId, NewClosureTime = DateTime.UtcNow.AddDays(2) });

		var events = await ReadAllAsync(aliceConnection.Reader, 1);
		Assert.Equal(LiveEventKind.AuctionUpdated, events[0].Kind);
		Assert.Equal(LiveEventType.Extended, events[0].Type);
		AssertDrained(aliceConnection.Reader);
	}

	[Fact]
	public async Task EventsWhileNoClientIsConnectedProduceNoMessages()
	{
		await using var harness = await Harness.Start();
		var auctionId = Guid.NewGuid();
		var alice = Guid.NewGuid();

		await harness.CreateAsync(new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "d", MinimumPrice = 10m, ClosureTime = DateTime.UtcNow.AddDays(1) });
		await harness.BidAsync(new BidPlaced { AuctionId = auctionId, BidderId = alice, MaximumAmount = 100m });

		// Connecting after the fact must not retroactively deliver anything.
		using var aliceConnection = harness.Hub.Connect(alice);
		AssertDrained(aliceConnection.Reader);
	}
}
