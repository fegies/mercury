using appcore.Entities;
using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public record BidResult(
	decimal CurrentBid,
	decimal? MyHighest,
	bool IsHighestBidder
);

public class PlaceBidEvaluator(decimal minBidIncrement) : IDecisionFunction<BidPlaced, BidResult>
{
	public EventSelector InitialSelector(BidPlaced input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<BidResult> Step(BidPlaced input, EventContext context)
	{
		var state = AuctionFold.State(context);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Cannot bid on a closed auction.");

		if (!state.IsPublished)
			throw new InvariantViolation("Cannot bid on an unpublished auction.");

		if (input.MaximumAmount <= 0)
			throw new InvariantViolation("Maximum bid must be positive.");

		var previousMax = AuctionState.MaxOfBidder(context, input.BidderId) ?? 0m;
		if (input.MaximumAmount < previousMax)
			throw new InvariantViolation("Cannot lower your previous bid");

		var currentPrice = BidPricing.Compute(state, minBidIncrement).CurrentPrice;
		if (input.MaximumAmount <= currentPrice)
			throw new InvariantViolation("Bid must exceed the current price.");

		var nextState = AuctionState.Incorporate(state, input);
		var (highestBidderId, nextPrice) = BidPricing.Compute(nextState, minBidIncrement);

		return new DecisionStep<BidResult>.Complete(
			new BidResult(nextPrice, input.MaximumAmount, highestBidderId == input.BidderId),
			[input]);
	}
}
