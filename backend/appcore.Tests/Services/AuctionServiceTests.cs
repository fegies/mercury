using appcore.Configuration;
using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Services;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Services;

public class AuctionServiceTests
{
	private static AuctionCreated CreateCreatedEvent(Guid? auctionId = null, bool? isPublished = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A description",
		MinimumPrice = 25.00m,
		ClosureTime = DateTime.UtcNow.AddDays(14),
		IsPublished = isPublished
	};	[Fact]
	public async Task GetAuctionState_ReturnsEmptyForUnknownId()
	{
		var store = new InMemoryEventStore([]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(Guid.NewGuid());

		Assert.Equal(Guid.Empty, state.AuctionId);
	}

	[Fact]
	public async Task GetAuctionState_ReconstructsFromCreatedEvent()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var store = new InMemoryEventStore([EventSerializer.Serialize(created)]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.Equal(auctionId, state.AuctionId);
		Assert.Equal("Test Auction", state.Title);
		Assert.Equal("A description", state.Description);
		Assert.Equal(25.00m, state.MinimumPrice);
		Assert.False(state.IsClosed);
		Assert.Empty(state.Images);
		Assert.True(state.IsPublished);
	}

	[Fact]
	public async Task GetAuctionState_DefaultsLegacyEventsToPublished()
	{
		var auctionId = Guid.NewGuid();
		var store = new InMemoryEventStore([EventSerializer.Serialize(CreateCreatedEvent(auctionId))]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.True(state.IsPublished);

		var summary = await service.GetAuctionSummary(auctionId);
		Assert.True(summary!.IsPublished);
	}

	[Fact]
	public async Task GetAuctionState_HonoursExplicitUnpublishedFlag()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId, isPublished: false);
		var store = new InMemoryEventStore([EventSerializer.Serialize(created)]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.False(state.IsPublished);
	}

	[Fact]
	public async Task GetAuctionState_PublishedFlagIncorporatesUpdate()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId, isPublished: false);
		var updated = new AuctionUpdated
		{
			AuctionId = auctionId,
			IsPublished = true,
		};
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created),
			EventSerializer.Serialize(updated),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.True(state.IsPublished);
	}

	[Fact]
	public async Task GetAuctionState_IncorporatesUpdate()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var updated = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Updated Title",
			MinimumPrice = 50.00m,
		};
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created),
			EventSerializer.Serialize(updated),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.Equal("Updated Title", state.Title);
		Assert.Equal("A description", state.Description);
		Assert.Equal(50.00m, state.MinimumPrice);
	}

	[Fact]
	public async Task GetAuctionState_IncorporatesImagesAddedAndRemoved()
	{
		var auctionId = Guid.NewGuid();
		var image1Id = Guid.NewGuid();
		var image2Id = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var imagesAdded = new AuctionImagesAdded
		{
			AuctionId = auctionId,
			Images =
			[
				new AuctionImageRef { Id = image1Id, FilePath = "/a.jpg", Hash = "h1" },
				new AuctionImageRef { Id = image2Id, FilePath = "/b.jpg", Hash = "h2" },
			]
		};
		var imagesRemoved = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [image1Id],
		};
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created),
			EventSerializer.Serialize(imagesAdded),
			EventSerializer.Serialize(imagesRemoved),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.Single(state.Images);
		Assert.Equal(image2Id, state.Images[0].Id);
	}

	[Fact]
	public async Task GetAuctionState_IncorporatesClosed()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var closed = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual,
		};
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created),
			EventSerializer.Serialize(closed),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.True(state.IsClosed);
	}

	[Fact]
	public async Task GetAuctionState_OrdersBySequenceId()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var update1 = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "First update",
		};
		var update2 = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Second update",
		};
		var rows = new List<AppEvent>
		{
			EventSerializer.Serialize(created) with { SequenceId = 1 },
			EventSerializer.Serialize(update1) with { SequenceId = 2 },
			EventSerializer.Serialize(update2) with { SequenceId = 3 },
		};
		var store = new InMemoryEventStore(rows);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.Equal("Second update", state.Title);
	}

	[Fact]
	public async Task GetAuctionState_ReverseSequenceId_ProducesDifferentResult()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var update1 = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "First update",
		};
		var update2 = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Second update",
		};
		var rows = new List<AppEvent>
		{
			EventSerializer.Serialize(created) with { SequenceId = 1 },
			EventSerializer.Serialize(update2) with { SequenceId = 2 },
			EventSerializer.Serialize(update1) with { SequenceId = 3 },
		};
		var store = new InMemoryEventStore(rows);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.Equal("First update", state.Title);
	}

	[Fact]
	public async Task GetAuctionState_IgnoresEventsForOtherAuctions()
	{
		var auctionId = Guid.NewGuid();
		var otherId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var otherCreated = CreateCreatedEvent(otherId);
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created),
			EventSerializer.Serialize(otherCreated),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var state = await service.GetAuctionState(auctionId);

		Assert.Equal(auctionId, state.AuctionId);
		Assert.Equal("Test Auction", state.Title);
	}

	[Fact]
	public async Task GetAllAuctionIds_ReturnsDistinctIds()
	{
		var id1 = Guid.NewGuid();
		var id2 = Guid.NewGuid();
		var created1 = CreateCreatedEvent(id1);
		var created2 = CreateCreatedEvent(id2);
		var updated1 = new AuctionUpdated { AuctionId = id1, Title = "Updated" };
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created1),
			EventSerializer.Serialize(created2),
			EventSerializer.Serialize(updated1),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var ids = await service.GetAllAuctionIds();

		Assert.Equal(2, ids.Count);
		Assert.Contains(id1, ids);
		Assert.Contains(id2, ids);
	}

	[Fact]
	public async Task GetAuctionSummary_ReturnsNullForUnknownId()
	{
		var store = new InMemoryEventStore([]);
		var service = new AuctionService(store, new AuctionConfig());

		var summary = await service.GetAuctionSummary(Guid.NewGuid());

		Assert.Null(summary);
	}

	[Fact]
	public async Task GetAuctionSummary_ReturnsCorrectSummary()
	{
		var auctionId = Guid.NewGuid();
		var imageId = Guid.NewGuid();
		var created = CreateCreatedEvent(auctionId);
		var imagesAdded = new AuctionImagesAdded
		{
			AuctionId = auctionId,
			Images = [new AuctionImageRef { Id = imageId, FilePath = "/img.jpg", Hash = "abc" }],
		};
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(created),
			EventSerializer.Serialize(imagesAdded),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var summary = await service.GetAuctionSummary(auctionId);

		Assert.NotNull(summary);
		Assert.Equal(auctionId, summary.Id);
		Assert.Equal("Test Auction", summary.Title);
		Assert.Single(summary.ImageUrls);
		Assert.Equal($"/api/auctions/{auctionId}/images/{imageId}", summary.ImageUrls[0]);
		Assert.False(summary.IsClosed);
		Assert.Null(summary.CurrentBid);
	}

	[Fact]
	public async Task ListAuctionSummaries_ReturnsAll()
	{
		var id1 = Guid.NewGuid();
		var id2 = Guid.NewGuid();
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(CreateCreatedEvent(id1)),
			EventSerializer.Serialize(CreateCreatedEvent(id2)),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var summaries = await service.ListAuctionSummaries();

		Assert.Equal(2, summaries.Count);
	}

	private static AppEvent Bid(Guid auctionId, Guid bidderId, decimal maximum, long seq)
		=> EventSerializer.Serialize(new BidPlaced
		{
			AuctionId = auctionId,
			BidderId = bidderId,
			MaximumAmount = maximum,
		}) with { SequenceId = seq };

	[Fact]
	public async Task GetAuctionSummary_PopulatesCurrentBidAndSelfVisibility()
	{
		var auctionId = Guid.NewGuid();
		var me = Guid.NewGuid();
		var other = Guid.NewGuid();
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(CreateCreatedEvent(auctionId)) with { SequenceId = 1 },
			Bid(auctionId, me, 100m, 2),
			Bid(auctionId, other, 60m, 3),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var summary = await service.GetAuctionSummary(auctionId, me);

		Assert.NotNull(summary);
		Assert.Equal(60.50m, summary.CurrentBid);
		Assert.Equal(100m, summary.MyHighest);
		Assert.True(summary.IsHighestBidder);
	}

	[Fact]
	public async Task GetAuctionSummary_ViewerBehindHighest_IsNotRevealedAsHighest()
	{
		var auctionId = Guid.NewGuid();
		var me = Guid.NewGuid();
		var leader = Guid.NewGuid();
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(CreateCreatedEvent(auctionId)) with { SequenceId = 1 },
			Bid(auctionId, leader, 100m, 2),
			Bid(auctionId, me, 60m, 3),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var summary = await service.GetAuctionSummary(auctionId, me);

		Assert.NotNull(summary);
		Assert.Equal(60.50m, summary.CurrentBid);
		Assert.Equal(60m, summary.MyHighest);
		Assert.False(summary.IsHighestBidder);
	}

	[Fact]
	public async Task GetAuctionSummary_UnrelatedViewer_SeesPriceButNoBidderInfo()
	{
		var auctionId = Guid.NewGuid();
		var leader = Guid.NewGuid();
		var viewer = Guid.NewGuid();
		var store = new InMemoryEventStore([
			EventSerializer.Serialize(CreateCreatedEvent(auctionId)) with { SequenceId = 1 },
			Bid(auctionId, leader, 100m, 2),
		]);
		var service = new AuctionService(store, new AuctionConfig());

		var summary = await service.GetAuctionSummary(auctionId, viewer);

		Assert.NotNull(summary);
		Assert.Equal(25.00m, summary.CurrentBid);
		Assert.Null(summary.MyHighest);
		Assert.False(summary.IsHighestBidder);
	}
}
