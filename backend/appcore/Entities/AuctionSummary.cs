namespace appcore.Entities;

public record AuctionSummary
{
	public Guid Id { get; init; }
	public string Title { get; init; } = "";
	public string Description { get; init; } = "";
	public decimal MinimumPrice { get; init; }
	public DateTime ClosureTime { get; init; }
	public bool IsClosed { get; init; }
	public List<string> ImageUrls { get; init; } = [];
	public decimal? CurrentBid { get; init; }
}
