using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using Xunit;

namespace appcore.Tests.Infrastructure;

public class InMemoryEventStoreTests
{
	private static AuctionCreated Created(Guid auctionId) => new()
	{
		AuctionId = auctionId,
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	private static AppEvent Event(StoredEvent e, long sequenceId)
		=> EventSerializer.Serialize(e) with { SequenceId = sequenceId };

	[Fact]
	public async Task Read_FiltersByTypesAndConstraints()
	{
		var auctionX = Guid.NewGuid();
		var auctionY = Guid.NewGuid();
		var store = new InMemoryEventStore([
			Event(Created(auctionX), 1),
			Event(Created(auctionY), 2),
		]);

		var context = await store.Read([EventSelector.ForAuction(auctionX, EventTypeNames.Auction)], CancellationToken.None);

		var row = Assert.Single(context.Events);
		Assert.Equal(auctionX.ToString(), row.Payload.RootElement.GetProperty("auctionId").GetString());
	}

	[Fact]
	public async Task Read_HeadReflectsGlobalMaxEvenWhenNothingMatches()
	{
		var store = new InMemoryEventStore([Event(Created(Guid.NewGuid()), 5)]);

		var context = await store.Read([EventSelector.ForAuction(Guid.NewGuid(), EventTypeNames.Auction)], CancellationToken.None);

		Assert.Empty(context.Events);
		Assert.Equal(5, context.Head);
	}

	[Fact]
	public async Task Append_AssignsMonotonicSequenceIds()
	{
		var store = new InMemoryEventStore();
		var input = Created(Guid.NewGuid());
		var boundary = ConsistencyBoundary.StartWith(EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction), head: 0);

		await store.Append([input], boundary, CancellationToken.None);

		var row = Assert.Single(store.Events);
		Assert.Equal(1, row.SequenceId);
	}

	[Fact]
	public async Task Append_SucceedsWhenBoundaryIsCurrent()
	{
		var auctionX = Guid.NewGuid();
		var auctionY = Guid.NewGuid();
		var store = new InMemoryEventStore([
			Event(Created(auctionX), 1),
			Event(Created(auctionY), 2),
		]);
		var input = Created(auctionX);
		var boundary = ConsistencyBoundary.StartWith(EventSelector.ForAuction(auctionX, EventTypeNames.Auction), head: 2);

		await store.Append([input], boundary, CancellationToken.None);

		Assert.Equal(3, store.Events.Count);
	}

	[Fact]
	public async Task Append_StaleBoundary_ThrowsConcurrencyConflict()
	{
		var auctionX = Guid.NewGuid();
		var store = new InMemoryEventStore([Event(Created(auctionX), 1)]);
		var input = Created(auctionX);
		var boundary = ConsistencyBoundary.StartWith(EventSelector.ForAuction(auctionX, EventTypeNames.Auction), head: 1);

		await store.Append([input], boundary, CancellationToken.None);
		store.Seed([Event(Created(auctionX), 3)]);

		await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
			store.Append([input], boundary, CancellationToken.None));
	}

	[Fact]
	public async Task Append_NoConflictForUnrelatedPartition()
	{
		var auctionX = Guid.NewGuid();
		var auctionY = Guid.NewGuid();
		var store = new InMemoryEventStore([Event(Created(auctionX), 1)]);
		var boundary = ConsistencyBoundary.StartWith(EventSelector.ForAuction(auctionY, EventTypeNames.Auction), head: 1);

		await store.Append([Created(auctionY)], boundary, CancellationToken.None);

		Assert.Equal(2, store.Events.Count);
	}
}
