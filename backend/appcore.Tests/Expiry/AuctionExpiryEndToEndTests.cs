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

/// <summary>
/// Full production path: store appends publish typed events to the bus, the expiry worker
/// schedules the deadline from those events, and once the clock passes it a close is appended
/// through the normal conflict-guarded handler.
/// </summary>
public class AuctionExpiryEndToEndTests
{
	[Fact]
	public async Task CreateBidThenExpireClosesTheAuctionWithWinnerAndPrice()
	{
		var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
		var bus = new InMemoryAppBus(NullLogger<InMemoryAppBus>.Instance);
		var store = new InMemoryEventStore(bus: bus);
		var provider = new ServiceCollection()
			.AddSingleton<IEventStore>(store)
			.AddSingleton<IEventReader>(store)
			.AddSingleton<IAppBus>(bus)
			.AddSingleton(new AuctionConfig())
			.AddSingleton(new EventHandlerOptions())
			.AddSingleton<IDecisionFunction<AuctionCreated, Guid>, CreateAuctionEvaluator>()
			.AddSingleton<IDecisionFunction<BidPlaced, BidResult>, PlaceBidEvaluator>(sp =>
				new PlaceBidEvaluator(sp.GetRequiredService<AuctionConfig>().MinBidIncrement))
			.AddSingleton<IDecisionFunction<AuctionClosed, bool>, CloseAuctionEvaluator>(sp =>
				new CloseAuctionEvaluator(sp.GetRequiredService<AuctionConfig>()))
			.AddSingleton<IncomingEventHandler<AuctionCreated, Guid>>()
			.AddSingleton<IncomingEventHandler<BidPlaced, BidResult>>()
			.AddSingleton<IncomingEventHandler<AuctionClosed, bool>>()
			.BuildServiceProvider();
		var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

		await bus.StartAsync(CancellationToken.None);
		var worker = new AuctionExpiryWorker(bus, scopeFactory, clock, NullLogger<AuctionExpiryWorker>.Instance);
		await worker.StartAsync(CancellationToken.None);

		var auctionId = Guid.NewGuid();
		var bidderId = Guid.NewGuid();
		var deadline = clock.GetUtcNow().UtcDateTime.AddHours(2);

		// Act: create an auction, place a high bid, then let the clock pass the closure time.
		await provider.GetRequiredService<IncomingEventHandler<AuctionCreated, Guid>>().Execute(
			new AuctionCreated { AuctionId = auctionId, Title = "Widget", Description = "A widget", MinimumPrice = 10m, ClosureTime = deadline },
			CancellationToken.None);
		await provider.GetRequiredService<IncomingEventHandler<BidPlaced, BidResult>>().Execute(
			new BidPlaced { AuctionId = auctionId, BidderId = bidderId, MaximumAmount = 100m },
			CancellationToken.None);

		clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromSeconds(1));

		// Assert: a close with the winning bidder and price was appended to the durable log.
		var closed = await WaitForClosed(store, auctionId);
		Assert.NotNull(closed);
		Assert.Equal(AuctionCloseReason.Expired, closed.Reason);
		Assert.Equal(bidderId, closed.WinnerUserId);
		Assert.Equal(10m, closed.WinningPrice);
		Assert.True(store.Events.Count >= 3);

		await worker.StopAsync(CancellationToken.None);
		await bus.StopAsync(CancellationToken.None);
	}

	private static async Task<AuctionClosed?> WaitForClosed(InMemoryEventStore store, Guid auctionId, int timeout = 5000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
		while (DateTime.UtcNow < deadline)
		{
			var closed = store.Events
				.Select(EventSerializer.Deserialize)
				.OfType<AuctionClosed>()
				.LastOrDefault(c => c.AuctionId == auctionId && c.Reason == AuctionCloseReason.Expired);
			if (closed is not null)
				return closed;
			await Task.Delay(10);
		}
		return null;
	}
}