using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
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
	public async Task Update_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Updated Title"
		};

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.True(result.Value);
	}

	[Fact]
	public async Task Update_ClosedAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(closedEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Updated Title"
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Cannot update a closed auction.", ex.Message);
	}

	[Fact]
	public async Task Update_NonexistentAuction_ThrowsInvariantViolation()
	{
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = Guid.NewGuid(),
			Title = "Updated Title"
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public async Task Update_EmptyTitle_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = ""
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Title must be non-empty.", ex.Message);
	}

	[Fact]
	public async Task Update_NegativePrice_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			MinimumPrice = -5m
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Minimum price must be non-negative.", ex.Message);
	}

	[Fact]
	public async Task Update_PastClosureTime_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			ClosureTime = DateTime.UtcNow.AddDays(-1)
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Closure time must be in the future.", ex.Message);
	}

	[Fact]
	public async Task Update_OnlyChangedFields_EmitsMinimalEvent()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new UpdateAuctionEvaluator(auctionService);
		var input = new AuctionUpdated
		{
			AuctionId = auctionId,
			Title = "Only Title Changed"
		};

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.Single(result.GeneratedEvents);
		var emitted = Assert.IsType<AuctionUpdated>(result.GeneratedEvents[0]);
		Assert.Equal("Only Title Changed", emitted.Title);
		Assert.Null(emitted.Description);
		Assert.Null(emitted.MinimumPrice);
		Assert.Null(emitted.ClosureTime);
	}
}
