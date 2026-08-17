using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;

namespace appcore.Services;

public class AuctionService(IContextProvider provider)
{
	public async Task<AuctionState> GetAuctionState(Guid auctionId)
	{
		var rows = await provider.QueryEvents();
		return rows
			.Where(r => r.Payload.RootElement.TryGetProperty("auctionId", out var prop)
					 && prop.GetString() == auctionId.ToString())
			.Select(EventSerializer.Deserialize)
			.Aggregate(AuctionState.Empty, AuctionState.Incorporate);
	}

	public async Task<List<Guid>> GetAllAuctionIds()
	{
		var rows = await provider.QueryEvents();
		return rows
			.Where(r => r.Payload.RootElement.TryGetProperty("auctionId", out _))
			.Select(r => r.Payload.RootElement.GetProperty("auctionId").GetGuid())
			.Distinct()
			.ToList();
	}

	public async Task<AuctionSummary?> GetAuctionSummary(Guid id)
	{
		var state = await GetAuctionState(id);
		if (state.AuctionId == Guid.Empty)
			return null;

		return MapToSummary(state);
	}

	public async Task<List<AuctionSummary>> ListAuctionSummaries()
	{
		var ids = await GetAllAuctionIds();
		var summaries = new List<AuctionSummary>();
		foreach (var id in ids)
		{
			var summary = await GetAuctionSummary(id);
			if (summary is not null)
				summaries.Add(summary);
		}
		return summaries;
	}

	public static AuctionSummary MapToSummary(AuctionState state)
	{
		return new AuctionSummary
		{
			Id = state.AuctionId,
			Title = state.Title,
			Description = state.Description,
			MinimumPrice = state.MinimumPrice,
			ClosureTime = state.ClosureTime,
			IsClosed = state.IsClosed,
			ImageUrls = state.Images
				.Select(i => $"/api/auctions/{state.AuctionId}/images/{i.Id}")
				.ToList(),
			CurrentBid = null,
		};
	}
}
