using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
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
	public void AddImages_OpenAuction_ReturnsTrue()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new AddImagesEvaluator();
		var input = CreateImagesEvent(auctionId);

		var step = evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent)));

		var result = Assert.IsType<DecisionStep<bool>.Complete>(step);
		Assert.True(result.Value);
	}

	[Fact]
	public void AddImages_ClosedAuction_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var closedEvent = new AuctionClosed
		{
			AuctionId = auctionId,
			Reason = AuctionCloseReason.Manual
		};
		var evaluator = new AddImagesEvaluator();
		var input = CreateImagesEvent(auctionId);

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent), EventSerializer.Serialize(closedEvent))));

		Assert.Equal("Cannot add images to a closed auction.", ex.Message);
	}

	[Fact]
	public void AddImages_NonexistentAuction_ThrowsInvariantViolation()
	{
		var evaluator = new AddImagesEvaluator();
		var input = CreateImagesEvent(Guid.NewGuid());

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Auction does not exist.", ex.Message);
	}

	[Fact]
	public void AddImages_EmptyList_ThrowsInvariantViolation()
	{
		var auctionId = Guid.NewGuid();
		var baseEvent = CreateBaseAppEvent(auctionId);
		var evaluator = new AddImagesEvaluator();
		var input = new AuctionImagesAdded
		{
			AuctionId = auctionId,
			Images = []
		};

		var ex = Assert.Throws<InvariantViolation>(() =>
			evaluator.Step(input, TestContext.From(EventSerializer.Serialize(baseEvent))));

		Assert.Equal("Must provide at least one image.", ex.Message);
	}
}
