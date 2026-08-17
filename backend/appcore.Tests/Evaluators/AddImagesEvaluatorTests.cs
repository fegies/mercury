using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class AddImagesEvaluatorTests
{
	private static AuctionCreated CreateBaseAppEvent(Guid? auctionId = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	private static AuctionImagesAdded CreateImagesEvent(Guid auctionId) => new()
	{
		AuctionId = auctionId,
		Images =
		[
			new AuctionImageRef { FilePath = "/img/1.jpg", Hash = "abc" },
			new AuctionImageRef { FilePath = "/img/2.jpg", Hash = "def" }
		]
	};

	[Fact]
	public async Task AddImages_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new AddImagesEvaluator(auctionService);
		var input = CreateImagesEvent(auctionId);

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.True(result.Value);
	}

	[Fact]
	public async Task AddImages_ClosedAuction_ThrowsInvariantViolation()
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
		var evaluator = new AddImagesEvaluator(auctionService);
		var input = CreateImagesEvent(auctionId);

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Cannot add images to a closed auction.", ex.Message);
	}

	[Fact]
	public async Task AddImages_NonexistentAuction_ThrowsInvariantViolation()
	{
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = new AddImagesEvaluator(auctionService);
		var input = CreateImagesEvent(Guid.NewGuid());

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public async Task AddImages_EmptyList_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new AddImagesEvaluator(auctionService);
		var input = new AuctionImagesAdded
		{
			AuctionId = auctionId,
			Images = []
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Must provide at least one image.", ex.Message);
	}
}
