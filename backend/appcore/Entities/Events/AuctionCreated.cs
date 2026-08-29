using System;

namespace appcore.Entities.Events;

public class AuctionCreated : StoredEvent
{
    public required Guid AuctionId { get; set; }

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal MinimumPrice { get; set; }
    public DateTime ClosureTime { get; set; } = DateTime.MaxValue;
    public bool? IsPublished { get; set; }
}
