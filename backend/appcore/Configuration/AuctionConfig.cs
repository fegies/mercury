namespace appcore.Configuration;

/// <summary>
/// Auction domain settings.
/// </summary>
public class AuctionConfig
{
	/// <summary>
	/// The minimum amount a new maximum bid must exceed the second-highest maximum by.
	/// </summary>
	public decimal MinBidIncrement { get; init; } = 0.50m;
}
