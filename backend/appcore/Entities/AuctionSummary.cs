namespace appcore.Entities;

public record AuctionSummary
{
	public required Guid Id { get; init; }
	public required string Title { get; init; }
	public required string Description { get; init; }
	public required decimal MinimumPrice { get; init; }
	public required DateTime ClosureTime { get; init; }
	public required bool IsClosed { get; init; }
	public required bool IsCancelled { get; init; }
	public required bool IsPublished { get; init; }
	public required List<string> ImageUrls { get; init; }
	public decimal? CurrentBid { get; init; }
	public decimal? MyHighest { get; init; }
	public bool IsHighestBidder { get; init; }
}
