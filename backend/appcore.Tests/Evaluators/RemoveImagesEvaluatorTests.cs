using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class RemoveImagesEvaluatorTests
{
	private static AuctionCreated CreateBaseAppEvent(Guid? auctionId = null) => new()
	{
		AuctionId = auctionId ?? Guid.NewGuid(),
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	private static (AuctionImagesAdded addedEvent, List<Guid> imageIds) AddImagesToAuction(Guid auctionId)
	{
		var image1Id = Guid.NewGuid();
		var image2Id = Guid.NewGuid();
		var addedEvent = new AuctionImagesAdded
		{
			AuctionId = auctionId,
			Images =
			[
				new AuctionImageRef { Id = image1Id, FilePath = "/img/1.jpg", Hash = "abc" },
				new AuctionImageRef { Id = image2Id, FilePath = "/img/2.jpg", Hash = "def" }
			]
		};
		return (addedEvent, [image1Id, image2Id]);
	}

	[Fact]
	public async Task RemoveImages_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var (addedEvent, imageIds) = AddImagesToAuction(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(addedEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new RemoveImagesEvaluator(auctionService);
		var input = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [imageIds[0]]
		};

		var result = await evaluator.EvaluateEventWithContext(input, CancellationToken.None);

		Assert.True(result.Value);
	}

	[Fact]
	public async Task RemoveImages_ClosedAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var (addedEvent, imageIds) = AddImagesToAuction(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(addedEvent), EventSerializer.Serialize(closedEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new RemoveImagesEvaluator(auctionService);
		var input = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [imageIds[0]]
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Cannot remove images from a closed auction.", ex.Message);
	}

	[Fact]
	public async Task RemoveImages_NonexistentImage_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var provider = new InMemoryContextProvider([EventSerializer.Serialize(baseEvent)]);
		var auctionService = new AuctionService(provider);
		var evaluator = new RemoveImagesEvaluator(auctionService);
		var input = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [Guid.NewGuid()]
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Contains("does not exist", ex.Message);
	}

	[Fact]
	public async Task RemoveImages_NonexistentAuction_ThrowsInvariantViolation()
	{
		var provider = new InMemoryContextProvider([]);
		var auctionService = new AuctionService(provider);
		var evaluator = new RemoveImagesEvaluator(auctionService);
		var input = new AuctionImagesRemoved
		{
			AuctionId = Guid.NewGuid(),
			ImageIds = [Guid.NewGuid()]
		};

		var ex = await Assert.ThrowsAsync<InvariantViolation>(() =>
			evaluator.EvaluateEventWithContext(input, CancellationToken.None));

		Assert.Equal("Auction does not exist.", ex.Message);
	}
}
