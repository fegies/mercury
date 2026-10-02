using appcore.Entities.Events;
using appcore.Infra.Evaluators;

namespace appcore.Infra.Live;

/// <summary>
/// State-free reconstruction of bid-derived facts from an auction's stored
/// events. Used by the live bridge instead of maintaining an in-memory mirror:
/// it re-folds the auction's log when an event needs to be attributed, and the
/// read snapshot may contain events appended after the triggering one.
/// </summary>
public static class OutbidDetection
{
	public sealed record Reconstruction(AuctionState Prior, AuctionState Current);

	/// <summary>
	/// Folds the auction's events in a single pass, snapshotting the state
	/// right before the incoming bid is incorporated (events appended after it
	/// cannot corrupt the snapshot) and the state after every event so far.
	/// Returns null when the incoming bid is not part of the read snapshot.
	/// </summary>
	public static Reconstruction? Reconstruct(EventContext context, BidPlaced incoming)
	{
		var result = context.Fold(
			(Prior: (AuctionState?)null, Current: AuctionState.Empty, Matched: false),
			(acc, e) =>
			{
				var current = AuctionState.Incorporate(acc.Current, e);
				return e is BidPlaced bid
					&& bid.BidderId == incoming.BidderId
					&& bid.MaximumAmount == incoming.MaximumAmount
						? (acc.Current, current, true)
						: (acc.Prior, current, acc.Matched);
			});

		return result.Matched ? new Reconstruction(result.Prior!, result.Current) : null;
	}

	/// <summary>
	/// The user that lost the lead to the incoming bid, or null when the bid
	/// did not take the lead away from anyone: first bid, self-raise, or an
	/// equal maximum that keeps the earlier bidder in front. A concurrent
	/// later overtake of someone else does not retroactively change who the
	/// incoming bid outbid.
	/// </summary>
	public static Guid? OutbidUser(Reconstruction reconstruction, Guid incomingBidderId)
		=> reconstruction.Prior.HighestBidderId is { } prior
			&& prior != incomingBidderId
			&& reconstruction.Current.HighestBidderId != prior
				? prior
				: null;

	/// <summary>
	/// Every user that ever placed a bid on the auction, in first-bid order.
	/// </summary>
	public static List<Guid> DistinctBidders(EventContext context)
		=> context.Fold(new List<Guid>(), (bidders, e) =>
		{
			if (e is BidPlaced bid && !bidders.Contains(bid.BidderId))
				bidders.Add(bid.BidderId);
			return bidders;
		});
}
