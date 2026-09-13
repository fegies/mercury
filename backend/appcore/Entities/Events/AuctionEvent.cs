using System;

namespace appcore.Entities.Events;

/// <summary>
/// Base for every persisted event that belongs to an auction; carries the
/// id of the auction it applies to.
/// </summary>
public abstract class AuctionEvent : StoredEvent
{
    public required Guid AuctionId { get; set; }
}