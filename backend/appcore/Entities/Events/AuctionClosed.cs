using System;

namespace appcore.Entities.Events;

public class AuctionClosed : StoredEvent
{
    public required Guid AuctionId { get; set; }

    public AuctionCloseReason Reason { get; set; }
}
