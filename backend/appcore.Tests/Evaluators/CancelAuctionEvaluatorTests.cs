using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class CancelAuctionEvaluatorTests
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
	public void Cancel_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new CancelAuctionEvaluator();
		var input = new AuctionCancelled
		{
			AuctionId = auctionId
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void Cancel_EmitsCancelledEvent()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new CancelAuctionEvaluator();
		var input = new AuctionCancelled
		{
			AuctionId = auctionId
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.Single(result.EventsToAppend);
		var cancelledEvent = Assert.IsType<AuctionCancelled>(result.EventsToAppend[0]);
		Assert.Equal(auctionId, cancelledEvent.AuctionId);
	}

	[Fact]
	public void Cancel_AlreadyClosed_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var evaluator = new CancelAuctionEvaluator();
		var input = new AuctionCancelled
		{
			AuctionId = auctionId
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(closedEvent))));

		Assert.Equal("Auction is already closed.", ex.Message);
	}

	[Fact]
	public void Cancel_NonexistentAuction_ThrowsInvariantViolation()
	{
		var evaluator = new CancelAuctionEvaluator();
		var input = new AuctionCancelled
		{
			AuctionId = Guid.NewGuid()
		};

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public void Cancel_PendingExtensionDoesNotAffectCancellation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var extended = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(14)
		};
		var evaluator = new CancelAuctionEvaluator();
		var input = new AuctionCancelled
		{
			AuctionId = auctionId
		};

		var step = evaluator.Step(input, TestContext.From(
			EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(extended)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}
}