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
	List<AuctionImageRef> Images
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
		AuctionClosed => state with
		{
			IsClosed = true,
		},
		_ => state,
	};

	public static readonly AuctionState Empty = new(
		AuctionId: Guid.Empty,
		Title: "",
		Description: "",
		MinimumPrice: 0m,
		ClosureTime: DateTime.MaxValue,
		IsClosed: false,
		IsPublished: true,
		Images: []
	);
}

public static class AuctionFold
{
	public static AuctionState State(EventContext context)
		=> context.Fold(AuctionState.Empty, AuctionState.Incorporate);
}
