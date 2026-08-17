using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class CreateAuctionEvaluatorTests
{
	private static CreateAuctionEvaluator CreateEvaluator(AuctionService auctionService)
	{
		return new CreateAuctionEvaluator(auctionService);
	}

	private static AuctionCreated CreateValidAppEvent(Guid? auctionId = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	[Fact]
	public async Task Create_ValidAuction_ReturnsAuctionId()
	{
		var auctionId = Guid.NewGuid();
		var input = CreateValidAppEvent(auctionId);
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = CreateEvaluator(auctionService);

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.Equal(auctionId, result.Value);
	}

	[Fact]
	public async Task Create_EmptyTitle_ThrowsInvariantViolation()
	{
		var input = CreateValidAppEvent();
		input.Title = "";
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = CreateEvaluator(auctionService);

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Title must be non-empty.", ex.Message);
	}

	[Fact]
	public async Task Create_NegativePrice_ThrowsInvariantViolation()
	{
		var input = CreateValidAppEvent();
		input.MinimumPrice = -5m;
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = CreateEvaluator(auctionService);

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Minimum price must be non-negative.", ex.Message);
	}

	[Fact]
	public async Task Create_PastClosureTime_ThrowsInvariantViolation()
	{
		var input = CreateValidAppEvent();
		input.ClosureTime = DateTime.UtcNow.AddDays(-1);
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = CreateEvaluator(auctionService);

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Closure time must be in the future.", ex.Message);
	}

	[Fact]
	public async Task Create_DuplicateAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var existing = CreateValidAppEvent(auctionId);

		var duplicate = CreateValidAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(existing)]);
		var auctionService = new AuctionService(provider);
		var evaluator = CreateEvaluator(auctionService);

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(duplicate, CancellationToken.None));

		Assert.Equal("An auction with this ID already exists.", ex.Message);
	}

	[Fact]
	public async Task Create_EmitsCorrectEvent()
	{
		var input = CreateValidAppEvent();
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = CreateEvaluator(auctionService);

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.Single(result.GeneratedEvents);
		Assert.Same(input, result.GeneratedEvents[0]);
	}
}
