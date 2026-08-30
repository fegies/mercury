namespace appcore.Infra.Evaluators;

public static class BidPricing
{
	/// <summary>
	/// Resolves the current highest bidder and the displayed current price from the
	/// per-bidder maxima. Equal maximums between distinct bidders are won by the earlier bidder.
	/// </summary>
	public static (Guid? HighestBidderId, decimal CurrentPrice) Compute(
		AuctionState state, decimal increment)
	{
		if (state.Maxima.Count == 0)
			return (null, 0m);

		var ranked = state.Maxima
			.Select(kv => (BidderId: kv.Key, Maximum: kv.Value, MaxAtSequence: state.MaxAtSequence.GetValueOrDefault(kv.Key)))
			.OrderByDescending(b => b.Maximum)
			.ThenBy(b => b.MaxAtSequence)
			.ToList();

		var highest = ranked[0];

		if (ranked.Count < 2)
			return (highest.BidderId, state.MinimumPrice);

		var secondHighest = ranked[1].Maximum;
		var price = Math.Max(state.MinimumPrice, secondHighest + increment);

		return (highest.BidderId, price);
	}
}
