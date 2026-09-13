using System;
using System.Collections.Generic;

namespace appcore.Entities.Events;

public class AuctionImagesAdded : AuctionEvent
{
    public List<AuctionImageRef> Images { get; set; } = [];
}
