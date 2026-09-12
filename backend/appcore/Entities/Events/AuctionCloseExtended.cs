using System;

namespace appcore.Entities.Events;

public class AuctionCloseExtended : StoredEvent
{
    public required Guid AuctionId { get; set; }

    public DateTime NewClosureTime { get; set; }
}
