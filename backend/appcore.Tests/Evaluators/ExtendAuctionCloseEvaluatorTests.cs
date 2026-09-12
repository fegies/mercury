using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class ExtendAuctionCloseEvaluatorTests
{
	private static AuctionCreated CreateBaseAppEvent(Guid? auctionId = null, DateTime? closureTime = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = closureTime ?? DateTime.UtcNow.AddDays(7)
	};

	[Fact]
	public void Extend_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(14)
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void Extend_EmitsEventWithNewClosureTime()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new ExtendAuctionCloseEvaluator();
		var newClosureTime = DateTime.UtcNow.AddDays(14);
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = newClosureTime
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.Single(result.EventsToAppend);
		var emitted = Assert.IsType<AuctionCloseExtended>(result.EventsToAppend[0]);
		Assert.Equal(newClosureTime, emitted.NewClosureTime);
	}

	[Fact]
	public void Extend_AfterPreviousExtension_AllowsFurtherExtension()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var firstExtension = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(14)
		};
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(21)
		};

		var step = evaluator.Step(input, TestContext.From(
			EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(firstExtension)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void Extend_CloserInTime_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(3)
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Closure time can only be extended.", ex.Message);
	}

	[Fact]
	public void Extend_UnchangedClosureTime_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var closureTime = DateTime.UtcNow.AddDays(7);
		var baseEvent = CreateBaseAppEvent(auctionId, closureTime);
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = closureTime
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Closure time can only be extended.", ex.Message);
	}

	[Fact]
	public void Extend_PastClosureTime_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(-1)
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Closure time must be in the future.", ex.Message);
	}

	[Fact]
	public void Extend_ClosedAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = auctionId,
			NewClosureTime = DateTime.UtcNow.AddDays(14)
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(closedEvent))));

		Assert.Equal("Cannot extend a closed auction.", ex.Message);
	}

	[Fact]
	public void Extend_NonexistentAuction_ThrowsInvariantViolation()
	{
		var evaluator = new ExtendAuctionCloseEvaluator();
		var input = new AuctionCloseExtended
		{
			AuctionId = Guid.NewGuid(),
			NewClosureTime = DateTime.UtcNow.AddDays(14)
		};

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Auction does not exist.", ex.Message);
	}
}