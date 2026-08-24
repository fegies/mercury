using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class UpdateAuctionEvaluatorTests
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
	public void Update_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Updated Title"
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void Update_ClosedAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Updated Title"
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(closedEvent))));

		Assert.Equal("Cannot update a closed auction.", ex.Message);
	}

	[Fact]
	public void Update_NonexistentAuction_ThrowsInvariantViolation()
	{
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = Guid.NewGuid(),
			Title = "Updated Title"
		};

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public void Update_EmptyTitle_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = ""
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Title must be non-empty.", ex.Message);
	}

	[Fact]
	public void Update_NegativePrice_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			MinimumPrice = -5m
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Minimum price must be non-negative.", ex.Message);
	}

	[Fact]
	public void Update_PastClosureTime_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			ClosureTime = DateTime.UtcNow.AddDays(-1)
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Closure time must be in the future.", ex.Message);
	}

	[Fact]
	public void Update_OnlyChangedFields_EmitsMinimalEvent()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new UpdateAuctionEvaluator();
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Only Title Changed"
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.Single(result.EventsToAppend);
		var emitted = Assert.IsType<AuctionUpdated>(result.EventsToAppend[0]);
		Assert.Equal("Only Title Changed", emitted.Title);
		Assert.Null(emitted.Description);
		Assert.Null(emitted.MinimumPrice);
		Assert.Null(emitted.ClosureTime);
	}
}
