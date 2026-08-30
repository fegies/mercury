using appcore.Configuration;
using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public class CloseAuctionEvaluator(AuctionConfig auctionConfig) : IDecisionFunction<AuctionClosed, bool>
{
	public EventSelector InitialSelector(AuctionClosed input)
		=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

	public DecisionStep<bool> Step(AuctionClosed input, EventContext context)
	{
		var state = AuctionFold.State(context);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Auction is already closed.");

		var (highestBidderId, currentPrice) = BidPricing.Compute(state, auctionConfig.MinBidIncrement);

		input.WinnerUserId = highestBidderId;
		input.WinningPrice = highestBidderId.HasValue ? currentPrice : null;

		return new DecisionStep<bool>.Complete(true, [input]);
	}
}
