using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Infra.Live;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Live;

public class OutbidDetectionTests
{
	private static readonly Guid AuctionId = Guid.NewGuid();
	private static readonly Guid AdminId = Guid.NewGuid();

	private static AppEvent Row(StoredEvent domainEvent, long sequenceId)
		=> EventSerializer.Serialize(domainEvent) with { SequenceId = sequenceId };

	private static EventContext Context(params AppEvent[] rows) => TestContext.From(rows);

	private static AppEvent[] AuctionWith(params BidPlaced[] bids)
	{
		var rows = new List<AppEvent> { Row(new AuctionCreated
		{
			AuctionId = AuctionId,
			Title = "Test",
			Description = "d",
			MinimumPrice = 10m,
			ClosureTime = DateTime.MaxValue,
		}, 1) };
		var sequence = 2L;
		foreach (var bid in bids)
			rows.Add(Row(bid, sequence++));
		return [.. rows];
	}

	private static BidPlaced Bid(Guid bidderId, decimal maximum) => new()
	{
		AuctionId = AuctionId,
		BidderId = bidderId,
		MaximumAmount = maximum,
	};

	[Fact]
	public void FirstBidOutbidsNobody()
	{
		var bidderId = Guid.NewGuid();
		var incoming = Bid(bidderId, 50m);
		var context = Context(AuctionWith(incoming));

		var reconstruction = OutbidDetection.Reconstruct(context, incoming);

		Assert.NotNull(reconstruction);
		Assert.Null(OutbidDetection.OutbidUser(reconstruction, bidderId));
	}

	[Fact]
	public void OvertakeReportsThePriorLeader()
	{
		var leaderId = Guid.NewGuid();
		var challengerId = Guid.NewGuid();
		var incoming = Bid(challengerId, 150m);
		var context = Context(AuctionWith(Bid(leaderId, 100m), incoming));

		var reconstruction = OutbidDetection.Reconstruct(context, incoming);

		Assert.NotNull(reconstruction);
		Assert.Equal(leaderId, OutbidDetection.OutbidUser(reconstruction, challengerId));
	}

	[Fact]
	public void SelfRaiseReportsNobody()
	{
		var leaderId = Guid.NewGuid();
		var incoming = Bid(leaderId, 150m);
		var context = Context(AuctionWith(Bid(leaderId, 100m), incoming));

		var reconstruction = OutbidDetection.Reconstruct(context, incoming);

		Assert.NotNull(reconstruction);
		Assert.Null(OutbidDetection.OutbidUser(reconstruction, leaderId));
	}

	[Fact]
	public void EqualMaximumKeepsTheEarlierBidderInFrontAndReportsNobody()
	{
		var leaderId = Guid.NewGuid();
		var challengerId = Guid.NewGuid();
		var incoming = Bid(challengerId, 100m);
		var context = Context(AuctionWith(Bid(leaderId, 100m), incoming));

		var reconstruction = OutbidDetection.Reconstruct(context, incoming);

		Assert.NotNull(reconstruction);
		Assert.Equal(leaderId, reconstruction.Current.HighestBidderId);
		Assert.Null(OutbidDetection.OutbidUser(reconstruction, challengerId));
	}

	[Fact]
	public void RepeatedOvertakesAttributeEachLossToItsOwnBid()
	{
		var alice = Guid.NewGuid();
		var bob = Guid.NewGuid();
		var carol = Guid.NewGuid();
		var first = Bid(bob, 150m);
		var second = Bid(carol, 200m);
		var context = Context(AuctionWith(Bid(alice, 100m), first, second));

		var firstReconstruction = OutbidDetection.Reconstruct(context, first);
		var secondReconstruction = OutbidDetection.Reconstruct(context, second);

		Assert.NotNull(firstReconstruction);
		Assert.NotNull(secondReconstruction);
		Assert.Equal(alice, OutbidDetection.OutbidUser(firstReconstruction, bob));
		Assert.Equal(bob, OutbidDetection.OutbidUser(secondReconstruction, carol));
	}

	[Fact]
	public void ConcurrentLaterOvertakeDoesNotHideWhoWasOutbid()
	{
		var leaderId = Guid.NewGuid();
		var challengerId = Guid.NewGuid();
		var thirdBidderId = Guid.NewGuid();
		var incoming = Bid(challengerId, 150m);
		var later = Bid(thirdBidderId, 200m);
		var context = Context(AuctionWith(Bid(leaderId, 100m), incoming, later));

		var reconstruction = OutbidDetection.Reconstruct(context, incoming);

		Assert.NotNull(reconstruction);
		Assert.Equal(thirdBidderId, reconstruction.Current.HighestBidderId);
		Assert.Equal(leaderId, OutbidDetection.OutbidUser(reconstruction, challengerId));
	}

	[Fact]
	public void BidThatOnlyTakesSecondPlaceOutbidsNobody()
	{
		var leaderId = Guid.NewGuid();
		var secondId = Guid.NewGuid();
		var incoming = Bid(secondId, 90m);
		var context = Context(AuctionWith(Bid(leaderId, 100m), incoming));

		var reconstruction = OutbidDetection.Reconstruct(context, incoming);

		Assert.NotNull(reconstruction);
		Assert.Null(OutbidDetection.OutbidUser(reconstruction, secondId));
	}

	[Fact]
	public void MissingIncomingBidYieldsNullReconstruction()
	{
		var incoming = Bid(AdminId, 50m);
		var context = Context(AuctionWith(Bid(Guid.NewGuid(), 100m)));

		Assert.Null(OutbidDetection.Reconstruct(context, incoming));
	}

	[Fact]
	public void DistinctBiddersListsEveryBidderOnceInFirstBidOrder()
	{
		var alice = Guid.NewGuid();
		var bob = Guid.NewGuid();
		var context = Context(AuctionWith(Bid(alice, 100m), Bid(bob, 90m), Bid(alice, 150m)));

		Assert.Equal([alice, bob], OutbidDetection.DistinctBidders(context));
	}
}
