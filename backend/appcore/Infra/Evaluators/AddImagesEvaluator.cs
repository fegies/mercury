using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class AddImagesEvaluator : IDecisionFunction<AuctionImagesAdded, bool>
{
	public EventSelector InitialSelector(AuctionImagesAdded input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<bool> Step(AuctionImagesAdded input, EventContext context)
	{
		var state = AuctionFold.State(context);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Cannot add images to a closed auction.");

		if (input.Images.Count == 0)
			throw new InvariantViolation("Must provide at least one image.");

		return new DecisionStep<bool>.Complete(true, [input]);
	}
}
