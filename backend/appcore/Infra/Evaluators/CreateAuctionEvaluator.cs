using appcore.Entities.Events;
using appcore.Services;

namespace appcore.Infra.Evaluators;

public class CreateAuctionEvaluator(AuctionService auctionService) : EventEvaluator<AuctionCreated, Guid>
{
	public async Task<EvaluatorResult<Guid>> EvaluateEventWithContext(
		AuctionCreated input,
		CancellationToken ct
	)
	{
		if (string.IsNullOrEmpty(input.Title))
			throw new InvariantViolation("Title must be non-empty.");

		if (input.MinimumPrice < 0)
			throw new InvariantViolation("Minimum price must be non-negative.");

		if (input.ClosureTime <= DateTime.UtcNow)
			throw new InvariantViolation("Closure time must be in the future.");

		var state = await auctionService.GetAuctionState(input.AuctionId);
		if (state.AuctionId != Guid.Empty)
			throw new InvariantViolation("An auction with this ID already exists.");

		return new EvaluatorResult<Guid>(input.AuctionId, [input]);
	}
}
