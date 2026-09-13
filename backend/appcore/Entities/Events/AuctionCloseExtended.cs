using System;

namespace appcore.Entities.Events;

public class AuctionCloseExtended : AuctionEvent
{
    public DateTime NewClosureTime { get; set; }
}
