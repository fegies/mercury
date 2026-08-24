using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class CreateAuctionEvaluator : IDecisionFunction<AuctionCreated, Guid>
{
	public EventSelector InitialSelector(AuctionCreated input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<Guid> Step(AuctionCreated input, EventContext context)
	{
		if (string.IsNullOrEmpty(input.Title))
			throw new InvariantViolation("Title must be non-empty.");

		if (input.MinimumPrice < 0)
			throw new InvariantViolation("Minimum price must be non-negative.");

		if (input.ClosureTime <= DateTime.UtcNow)
			throw new InvariantViolation("Closure time must be in the future.");

		var state = AuctionFold.State(context);
		if (state.AuctionId != Guid.Empty)
			throw new InvariantViolation("An auction with this ID already exists.");

		return new DecisionStep<Guid>.Complete(input.AuctionId, [input]);
	}
}
