namespace appcore.Infra.Evaluators;

public static class BidPricing
{
	/// <summary>
	/// Resolves the current highest bidder and the displayed current price from the
	/// top-two maxima. The price is the second-highest maximum plus the minimum increment,
	/// floored at the auction's minimum price (or the minimum price when only one bidder exists).
	/// </summary>
	public static (Guid? HighestBidderId, decimal CurrentPrice) Compute(
		AuctionState state, decimal increment)
	{
		var price = state.SecondBidderId.HasValue
			? Math.Max(state.MinimumPrice, state.SecondMax + increment)
			: state.MinimumPrice;
		return (state.HighestBidderId, price);
	}
}
