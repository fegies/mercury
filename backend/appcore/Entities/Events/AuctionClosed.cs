using System;

namespace appcore.Entities.Events;

public class AuctionClosed : AuctionEvent
{
    public AuctionCloseReason Reason { get; set; }

    public Guid? WinnerUserId { get; set; }

    public decimal? WinningPrice { get; set; }
}
