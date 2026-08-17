using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
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
	public async Task Close_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new CloseAuctionEvaluator(auctionService);
		var input = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.True(result.Value);
	}

	[Fact]
	public async Task Close_AlreadyClosed_ThrowsInvariantViolation()
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
		var evaluator = new CloseAuctionEvaluator(auctionService);
		var input = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Expired
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Auction is already closed.", ex.Message);
	}

	[Fact]
	public async Task Close_NonexistentAuction_ThrowsInvariantViolation()
	{
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = new CloseAuctionEvaluator(auctionService);
		var input = new AuctionClosed
		{
			AuctionId = Guid.NewGuid(),
			Reason = AuctionCloseReason.Manual
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public async Task Close_EmitsCorrectReason()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new CloseAuctionEvaluator(auctionService);
		var input = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.Single(result.GeneratedEvents);
		var closedEvent = Assert.IsType<AuctionClosed>(result.GeneratedEvents[0]);
		Assert.Equal(AuctionCloseReason.Manual, closedEvent.Reason);
	}
}
