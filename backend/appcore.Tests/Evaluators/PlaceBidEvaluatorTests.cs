using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class PlaceBidEvaluatorTests
{
	private const decimal Increment = 0.50m;

	private static AuctionCreated CreateAuction(Guid auctionId, decimal minimumPrice = 10.00m) => new()
	{
		AuctionId = auctionId,
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = minimumPrice,
		ClosureTime = DateTime.UtcNow.AddDays(7),
		IsPublished = true,
	};

	private static AppEvent Auction(Guid auctionId, long seq, decimal minimumPrice = 10.00m)
		=> EventSerializer.Serialize(CreateAuction(auctionId, minimumPrice)) with { SequenceId = seq };

	private static AppEvent Bid(Guid auctionId, Guid bidderId, decimal maximum, long seq)
		=> EventSerializer.Serialize(new BidPlaced
		{
			AuctionId = auctionId,
			BidderId = bidderId,
			MaximumAmount = maximum,
		}) with { SequenceId = seq };

	private static PlaceBidEvaluator Evaluator() => new(Increment);

	[Fact]
	public void Step_FirstBid_AppendsAndFloorsAtMinimumPrice()
	{
		var auctionId = Guid.NewGuid();
		var bidder = Guid.NewGuid();
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = bidder, MaximumAmount = 25m };

		var step = evaluator.Step(input, TestContext.From(Auction(auctionId, 1)));

		var result = Assert.IsType<DecisionStep<BidResult>.Complete>(step);
		Assert.Equal(10.00m, result.Value.CurrentBid);
		Assert.Equal(25m, result.Value.MyHighest);
		Assert.True(result.Value.IsHighestBidder);
		Assert.Single(result.EventsToAppend);
		Assert.Same(input, result.EventsToAppend[0]);
	}

	[Fact]
	public void Step_BidAtOrBelowCurrentPrice_Throws()
	{
		var auctionId = Guid.NewGuid();
		var bidder = Guid.NewGuid();
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = bidder, MaximumAmount = 10m };

		var context = TestContext.From(
			Auction(auctionId, 1),
			Bid(auctionId, Guid.NewGuid(), 100m, 2));

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, context));
		Assert.Equal("Bid must exceed the current price.", ex.Message);
	}

	[Fact]
	public void Step_OutbidExistingHigherMax_RaisesPriceToLeaderPlusIncrement()
	{
		var auctionId = Guid.NewGuid();
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var c = Guid.NewGuid();
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = c, MaximumAmount = 120m };

		var step = evaluator.Step(input, TestContext.From(
			Auction(auctionId, 1),
			Bid(auctionId, a, 100m, 2),
			Bid(auctionId, b, 50m, 3)));

		var result = Assert.IsType<DecisionStep<BidResult>.Complete>(step);
		Assert.Equal(100.50m, result.Value.CurrentBid);
		Assert.True(result.Value.IsHighestBidder);
		Assert.Equal(120m, result.Value.MyHighest);
	}

	[Fact]
	public void Step_RaiseOwnMaxBehindLeader_RaisesPriceButNotLeader()
	{
		var auctionId = Guid.NewGuid();
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = b, MaximumAmount = 90m };

		var step = evaluator.Step(input, TestContext.From(
			Auction(auctionId, 1),
			Bid(auctionId, a, 100m, 2),
			Bid(auctionId, b, 50m, 3)));

		var result = Assert.IsType<DecisionStep<BidResult>.Complete>(step);
		Assert.Equal(90.50m, result.Value.CurrentBid);
		Assert.False(result.Value.IsHighestBidder);
		Assert.Equal(90m, result.Value.MyHighest);
	}

	[Fact]
	public void Step_EqualMax_PreservesEarlierBidderAsHighest()
	{
		var auctionId = Guid.NewGuid();
		var a = Guid.NewGuid();
		var b = Guid.NewGuid();
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = b, MaximumAmount = 100m };

		var step = evaluator.Step(input, TestContext.From(
			Auction(auctionId, 1),
			Bid(auctionId, a, 100m, 2)));

		var result = Assert.IsType<DecisionStep<BidResult>.Complete>(step);
		Assert.Equal(100.50m, result.Value.CurrentBid);
		Assert.False(result.Value.IsHighestBidder);
	}

	[Fact]
	public void Step_ClosedAuction_Throws()
	{
		var auctionId = Guid.NewGuid();
		var closed = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual,
			WinnerUserId = Guid.NewGuid(),
			WinningPrice = 10m,
		};
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = Guid.NewGuid(), MaximumAmount = 25m };

		var context = TestContext.From(
			Auction(auctionId, 1),
			EventSerializer.Serialize(closed) with { SequenceId = 2 });

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, context));
		Assert.Equal("Cannot bid on a closed auction.", ex.Message);
	}

	[Fact]
	public void Step_UnpublishedAuction_Throws()
	{
		var auctionId = Guid.NewGuid();
		var created = CreateAuction(auctionId);
		created.IsPublished = false;
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = auctionId, BidderId = Guid.NewGuid(), MaximumAmount = 25m };

		var context = TestContext.From(EventSerializer.Serialize(created) with { SequenceId = 1 });

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, context));
		Assert.Equal("Cannot bid on an unpublished auction.", ex.Message);
	}

	[Fact]
	public void Step_NonexistentAuction_Throws()
	{
		var evaluator = Evaluator();
		var input = new BidPlaced { AuctionId = Guid.NewGuid(), BidderId = Guid.NewGuid(), MaximumAmount = 25m };

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));
		Assert.Equal("Auction does not exist.", ex.Message);
	}
}
