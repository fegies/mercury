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
	IReadOnlyDictionary<Guid, decimal> Maxima,
	IReadOnlyDictionary<Guid, long> MaxAtSequence,
	Guid? WinnerUserId,
	decimal? WinningPrice
)
{
	public static AuctionState Incorporate(AuctionState state, long sequenceId, StoredEvent e) => e switch
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
		BidPlaced bid => state with
		{
			Maxima = IncorporateMax(state.Maxima, bid.BidderId, bid.MaximumAmount),
			MaxAtSequence = IncorporateMaxAtSequence(state, bid.BidderId, bid.MaximumAmount, sequenceId),
		},
		_ => state,
	};

	private static IReadOnlyDictionary<Guid, decimal> IncorporateMax(
		IReadOnlyDictionary<Guid, decimal> maxima, Guid bidderId, decimal amount)
	{
		if (maxima.TryGetValue(bidderId, out var existing) && existing >= amount)
			return maxima;
		return new Dictionary<Guid, decimal>(maxima) { [bidderId] = amount };
	}

	private static IReadOnlyDictionary<Guid, long> IncorporateMaxAtSequence(
		AuctionState state, Guid bidderId, decimal amount, long sequenceId)
	{
		if (state.Maxima.TryGetValue(bidderId, out var existing) && existing >= amount)
			return state.MaxAtSequence;
		return new Dictionary<Guid, long>(state.MaxAtSequence) { [bidderId] = sequenceId };
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
		Maxima: new Dictionary<Guid, decimal>(),
		MaxAtSequence: new Dictionary<Guid, long>(),
		WinnerUserId: null,
		WinningPrice: null
	);
}

public static class AuctionFold
{
	public static AuctionState State(EventContext context)
		=> context.Fold(AuctionState.Empty, AuctionState.Incorporate);
}
