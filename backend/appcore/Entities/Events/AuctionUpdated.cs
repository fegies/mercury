using System;

namespace appcore.Entities.Events;

public class AuctionUpdated : AuctionEvent
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public decimal? MinimumPrice { get; set; }
    public bool? IsPublished { get; set; }
}
