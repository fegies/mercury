using appcore.Entities.Events;
using appcore.Services;

namespace appcore.Infra.Evaluators;

public class CloseAuctionEvaluator(AuctionService auctionService) : EventEvaluator<AuctionClosed, bool>
{
	public async Task<EvaluatorResult<bool>> EvaluateEventWithContext(
		AuctionClosed input,
		CancellationToken ct
	)
	{
		var state = await auctionService.GetAuctionState(input.AuctionId);

		if (state.AuctionId == Guid.Empty)
			throw new InvariantViolation("Auction does not exist.");

		if (state.IsClosed)
			throw new InvariantViolation("Auction is already closed.");

		return new EvaluatorResult<bool>(true, [input]);
	}
}
