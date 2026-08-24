using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class CloseAuctionEvaluatorTests
{
	private static AuctionCreated CreateBaseAppEvent(Guid? auctionId = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	[Fact]
	public void Close_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new CloseAuctionEvaluator();
		var input = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void Close_AlreadyClosed_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var evaluator = new CloseAuctionEvaluator();
		var input = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Expired
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(closedEvent))));

		Assert.Equal("Auction is already closed.", ex.Message);
	}

	[Fact]
	public void Close_NonexistentAuction_ThrowsInvariantViolation()
	{
		var evaluator = new CloseAuctionEvaluator();
		var input = new AuctionClosed
		{
			AuctionId = Guid.NewGuid(),
			Reason = AuctionCloseReason.Manual
		};

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public void Close_EmitsCorrectReason()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new CloseAuctionEvaluator();
		var input = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.Single(result.EventsToAppend);
		var closedEvent = Assert.IsType<AuctionClosed>(result.EventsToAppend[0]);
		Assert.Equal(AuctionCloseReason.Manual, closedEvent.Reason);
	}
}
