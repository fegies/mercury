using System;

namespace appcore.Entities.Events;

public class AuctionCreated : StoredEvent
{
    public Guid AuctionId { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "";
    public DateTime ClosureTime { get; set; } = DateTime.MaxValue;
}
