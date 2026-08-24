using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class RemoveImagesEvaluator : IDecisionFunction<AuctionImagesRemoved, bool>
{
	public EventSelector InitialSelector(AuctionImagesRemoved input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<bool> Step(AuctionImagesRemoved input, EventContext context)
	{
		var state = AuctionFold.State(context);

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

		return new DecisionStep<bool>.Complete(true, [input]);
	}
}
