using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class CancelAuctionEvaluator : IDecisionFunction<AuctionCancelled, bool>
{
	public EventSelector InitialSelector(AuctionCancelled input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<bool> Step(AuctionCancelled input, EventContext context)
	{
		var state = AuctionFold.State(context);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Auction is already closed.");

		return new DecisionStep<bool>.Complete(true, [input]);
	}
}