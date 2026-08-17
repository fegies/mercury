using appcore.Entities.Events;
using appcore.Services;

namespace appcore.Infra.Evaluators;

public class RemoveImagesEvaluator(AuctionService auctionService) : EventEvaluator<AuctionImagesRemoved, bool>
{
	public async Task<EvaluatorResult<bool>> EvaluateEventWithContext(
		AuctionImagesRemoved input,
		CancellationToken ct
	)
	{
		var state = await auctionService.GetAuctionState(input.AuctionId);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Cannot remove images from a closed auction.");

		var currentImageIds = state.Images.Select(i => i.Id).ToHashSet();
		foreach (var imageId in input.ImageIds)
		{
			if (!currentImageIds.Contains(imageId))
				throw new InvariantViolation($"Image with ID {imageId} does not exist.");
		}

		return new EvaluatorResult<bool>(true, [input]);
	}
}
