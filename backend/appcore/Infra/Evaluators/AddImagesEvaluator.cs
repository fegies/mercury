using appcore.Entities.Events;
using appcore.Services;

namespace appcore.Infra.Evaluators;

public class AddImagesEvaluator(AuctionService auctionService) : EventEvaluator<AuctionImagesAdded, bool>
{
	public async Task<EvaluatorResult<bool>> EvaluateEventWithContext(
		AuctionImagesAdded input,
		CancellationToken ct
	)
	{
		var state = await auctionService.GetAuctionState(input.AuctionId);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Cannot add images to a closed auction.");

		if (input.Images.Count == 0)
			throw new InvariantViolation("Must provide at least one image.");

		return new EvaluatorResult<bool>(true, [input]);
	}
}
