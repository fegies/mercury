using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
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
	public void RemoveImages_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var (addedEvent, imageIds) = AddImagesToAuction(auctionId);
		var evaluator = new RemoveImagesEvaluator();
		var input = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [imageIds[0]]
		};

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(addedEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void RemoveImages_ClosedAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var (addedEvent, imageIds) = AddImagesToAuction(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var evaluator = new RemoveImagesEvaluator();
		var input = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [imageIds[0]]
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(
				EventSerializer.Serialize(baseEvent),
				EventSerializer.Serialize(addedEvent),
				EventSerializer.Serialize(closedEvent))));

		Assert.Equal("Cannot remove images from a closed auction.", ex.Message);
	}

	[Fact]
	public void RemoveImages_NonexistentImage_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new RemoveImagesEvaluator();
		var input = new AuctionImagesRemoved
		{
			AuctionId = auctionId,
			ImageIds = [Guid.NewGuid()]
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Contains("does not exist", ex.Message);
	}

	[Fact]
	public void RemoveImages_NonexistentAuction_ThrowsInvariantViolation()
	{
		var evaluator = new RemoveImagesEvaluator();
		var input = new AuctionImagesRemoved
		{
			AuctionId = Guid.NewGuid(),
			ImageIds = [Guid.NewGuid()]
		};

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Auction does not exist.", ex.Message);
	}
}
