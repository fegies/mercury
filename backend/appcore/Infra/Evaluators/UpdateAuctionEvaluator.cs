using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class UpdateAuctionEvaluator : IDecisionFunction<AuctionUpdated, bool>
{
	public EventSelector InitialSelector(AuctionUpdated input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<bool> Step(AuctionUpdated input, EventContext context)
	{
		var state = AuctionFold.State(context);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Cannot update a closed auction.");

		if (input.Title is not null && string.IsNullOrEmpty(input.Title))
			throw new InvariantViolation("Title must be non-empty.");

		if (input.MinimumPrice is { } price && price < 0)
			throw new InvariantViolation("Minimum price must be non-negative.");

		if (input.ClosureTime is { } closure && closure <= DateTime.UtcNow)
			throw new InvariantViolation("Closure time must be in the future.");

		return new DecisionStep<bool>.Complete(true, [input]);
	}
}
