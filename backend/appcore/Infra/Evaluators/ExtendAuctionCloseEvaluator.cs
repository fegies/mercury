using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class ExtendAuctionCloseEvaluator : IDecisionFunction<AuctionCloseExtended, bool>
{
	public EventSelector InitialSelector(AuctionCloseExtended input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<bool> Step(AuctionCloseExtended input, EventContext context)
	{
		var state = AuctionFold.State(context);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Cannot extend a closed auction.");

		if (input.NewClosureTime <= DateTime.UtcNow)
			throw new InvariantViolation("Closure time must be in the future.");

		if (input.NewClosureTime <= state.ClosureTime)
			throw new InvariantViolation("Closure time can only be extended.");

		return new DecisionStep<bool>.Complete(true, [input]);
	}
}