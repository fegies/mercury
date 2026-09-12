using System;

namespace appcore.Entities.Events;

public class AuctionUpdated : StoredEvent
{
    public required Guid AuctionId { get; set; }

    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? MinimumPrice { get; set; }
    public bool? IsPublished { get; set; }
}
