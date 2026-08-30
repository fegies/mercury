using appcore.Configuration;
using appcore.Entities;
using appcore.Infra;
using appcore.Infra.Evaluators;

namespace appcore.Services;

public class AuctionService(IEventReader reader, AuctionConfig auctionConfig)
{
	public async Task<AuctionState> GetAuctionState(Guid auctionId)
	{
		var context = await reader.Read(
			[EventSelector.ForAuction(auctionId, EventTypeNames.Auction)],
			CancellationToken.None);
		return AuctionFold.State(context);
	}

	public async Task<List<Guid>> GetAllAuctionIds()
	{
		var context = await reader.Read(
			[EventSelector.OfTypes(EventTypeNames.AuctionCreated)],
			CancellationToken.None);
		return context.Events
			.Where(r => r.Payload.RootElement.TryGetProperty("auctionId", out _))
			.Select(r => r.Payload.RootElement.GetProperty("auctionId").GetGuid())
			.Distinct()
			.ToList();
	}

	public async Task<AuctionSummary?> GetAuctionSummary(Guid id, Guid? viewerId = null)
	{
		var context = await reader.Read(
			[EventSelector.ForAuction(id, EventTypeNames.Auction)],
			CancellationToken.None);
		var state = AuctionFold.State(context);
		if (state.AuctionId == Guid.Empty)
			return null;

		return MapToSummary(state, context, viewerId);
	}

	public async Task<List<AuctionSummary>> ListAuctionSummaries(Guid? viewerId = null)
	{
		var ids = await GetAllAuctionIds();
		var summaries = new List<AuctionSummary>();
		foreach (var id in ids)
		{
			var summary = await GetAuctionSummary(id, viewerId);
			if (summary is not null)
				summaries.Add(summary);
		}
		return summaries;
	}

	public AuctionSummary MapToSummary(AuctionState state, EventContext context, Guid? viewerId = null)
	{
		var (highestBidderId, currentPrice) = BidPricing.Compute(state, auctionConfig.MinBidIncrement);

		var myHighest = viewerId.HasValue ? AuctionState.MaxOfBidder(context, viewerId.Value) : null;
		var isHighestBidder = highestBidderId == viewerId;

		return new AuctionSummary
		{
			Id = state.AuctionId,
			Title = state.Title,
			Description = state.Description,
			MinimumPrice = state.MinimumPrice,
			ClosureTime = state.ClosureTime,
			IsClosed = state.IsClosed,
			IsPublished = state.IsPublished,
			ImageUrls = state.Images
				.Select(i => $"/api/auctions/{state.AuctionId}/images/{i.Id}")
				.ToList(),
			CurrentBid = state.HighestBidderId.HasValue ? currentPrice : null,
			MyHighest = myHighest,
			IsHighestBidder = isHighestBidder,
		};
	}
}
