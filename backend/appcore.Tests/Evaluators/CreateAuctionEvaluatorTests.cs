using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class CreateAuctionEvaluatorTests
{
	private static AuctionCreated CreateValidAppEvent(Guid? auctionId = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	[Fact]
	public void Create_ValidAuction_ReturnsAuctionId()
	{
		var auctionId = Guid.NewGuid();
		var input = CreateValidAppEvent(auctionId);
		var evaluator = new CreateAuctionEvaluator();

		var step = evaluator.Step(input, TestContext.From());

		var result = Assert.IsType<DecisionStep<Guid>.Complete>(step);
		Assert.Equal(auctionId, result.Value);
	}

	[Fact]
	public void Create_EmptyTitle_ThrowsInvariantViolation()
	{
		var input = CreateValidAppEvent();
		input.Title = "";
		var evaluator = new CreateAuctionEvaluator();

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Title must be non-empty.", ex.Message);
	}

	[Fact]
	public void Create_NegativePrice_ThrowsInvariantViolation()
	{
		var input = CreateValidAppEvent();
		input.MinimumPrice = -5m;
		var evaluator = new CreateAuctionEvaluator();

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Minimum price must be non-negative.", ex.Message);
	}

	[Fact]
	public void Create_PastClosureTime_ThrowsInvariantViolation()
	{
		var input = CreateValidAppEvent();
		input.ClosureTime = DateTime.UtcNow.AddDays(-1);
		var evaluator = new CreateAuctionEvaluator();

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Closure time must be in the future.", ex.Message);
	}

	[Fact]
	public void Create_DuplicateAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var existing = CreateValidAppEvent(auctionId);
		var duplicate = CreateValidAppEvent(auctionId);
		var evaluator = new CreateAuctionEvaluator();

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(duplicate, TestContext.From(EventSerializer.Serialize(existing))));

		Assert.Equal("An auction with this ID already exists.", ex.Message);
	}

	[Fact]
	public void Create_EmitsCorrectEvent()
	{
		var input = CreateValidAppEvent();
		var evaluator = new CreateAuctionEvaluator();

		var step = evaluator.Step(input, TestContext.From());

		var result = Assert.IsType<DecisionStep<Guid>.Complete>(step);
		Assert.Single(result.EventsToAppend);
		Assert.Same(input, result.EventsToAppend[0]);
	}
}
