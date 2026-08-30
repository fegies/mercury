using appcore.Entities;
using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public record AuctionState(
	Guid AuctionId,
	string Title,
	string Description,
	decimal MinimumPrice,
	DateTime ClosureTime,
	bool IsClosed,
	bool IsPublished,
	List<AuctionImageRef> Images,
	Guid? HighestBidderId,
	decimal HighestMax,
	Guid? SecondBidderId,
	decimal SecondMax,
	Guid? WinnerUserId,
	decimal? WinningPrice
)
{
	public static AuctionState Incorporate(AuctionState state, StoredEvent e) => e switch
	{
		AuctionCreated created => state with
		{
			AuctionId = created.AuctionId,
			Title = created.Title,
			Description = created.Description,
			MinimumPrice = created.MinimumPrice,
			ClosureTime = created.ClosureTime,
			IsPublished = created.IsPublished ?? state.IsPublished,
		},
		AuctionUpdated updated => state with
		{
			Title = updated.Title ?? state.Title,
			Description = updated.Description ?? state.Description,
			MinimumPrice = updated.MinimumPrice ?? state.MinimumPrice,
			ClosureTime = updated.ClosureTime ?? state.ClosureTime,
			IsPublished = updated.IsPublished ?? state.IsPublished,
		},
		AuctionImagesAdded added => state with
		{
			Images = [.. state.Images, .. added.Images],
		},
		AuctionImagesRemoved removed => state with
		{
			Images = state.Images.Where(i => !removed.ImageIds.Contains(i.Id)).ToList(),
		},
		AuctionClosed closed => state with
		{
			IsClosed = true,
			WinnerUserId = closed.WinnerUserId,
			WinningPrice = closed.WinningPrice,
		},
		BidPlaced bid => IncorporateBid(state, bid.BidderId, bid.MaximumAmount),
		_ => state,
	};

	private static AuctionState IncorporateBid(AuctionState state, Guid bidderId, decimal amount)
	{
		// Raising the current highest bidder: only their max changes; ranks and price are unchanged.
		if (state.HighestBidderId == bidderId)
			return state with { HighestMax = Math.Max(state.HighestMax, amount) };

		// Raising the second-highest bidder: their max may overtake the leader.
		if (state.SecondBidderId == bidderId)
		{
			var newSecond = Math.Max(state.SecondMax, amount);
			if (newSecond > state.HighestMax)
			{
				return state with
				{
					SecondBidderId = state.HighestBidderId,
					SecondMax = state.HighestMax,
					HighestBidderId = bidderId,
					HighestMax = newSecond,
				};
			}
			return state with { SecondMax = newSecond };
		}

		// New or previously lower-ranked bidder.
		if (amount > state.HighestMax)
		{
			return state with
			{
				SecondBidderId = state.HighestBidderId,
				SecondMax = state.HighestMax,
				HighestBidderId = bidderId,
				HighestMax = amount,
			};
		}

		if (amount > state.SecondMax)
		{
			return state with
			{
				SecondBidderId = bidderId,
				SecondMax = amount,
			};
		}

		return state;
	}

	/// <summary>
	/// The highest maximum placed by a single bidder, or null if that bidder has never bid.
	/// </summary>
	public static decimal? MaxOfBidder(EventContext context, Guid bidderId)
	{
		var max = context.Fold(decimal.MinValue, (acc, e) =>
			e is BidPlaced b && b.BidderId == bidderId ? Math.Max(acc, b.MaximumAmount) : acc);
		return max == decimal.MinValue ? null : max;
	}

	public static readonly AuctionState Empty = new(
		AuctionId: Guid.Empty,
		Title: "",
		Description: "",
		MinimumPrice: 0m,
		ClosureTime: DateTime.MaxValue,
		IsClosed: false,
		IsPublished: true,
		Images: [],
		HighestBidderId: null,
		HighestMax: 0m,
		SecondBidderId: null,
		SecondMax: 0m,
		WinnerUserId: null,
		WinningPrice: null
	);
}

public static class AuctionFold
{
	public static AuctionState State(EventContext context)
		=> context.Fold(AuctionState.Empty, AuctionState.Incorporate);
}
