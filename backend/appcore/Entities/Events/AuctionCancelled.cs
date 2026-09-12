using System;

namespace appcore.Entities.Events;

public class AuctionCancelled : StoredEvent
{
    public required Guid AuctionId { get; set; }
}