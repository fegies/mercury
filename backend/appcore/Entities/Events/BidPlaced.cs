using System;

namespace appcore.Entities.Events;

public class BidPlaced : AuctionEvent
{
	public required Guid BidderId { get; set; }

	public required decimal MaximumAmount { get; set; }

	public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
