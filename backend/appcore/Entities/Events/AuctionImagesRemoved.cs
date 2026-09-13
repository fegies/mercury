using System;
using System.Collections.Generic;

namespace appcore.Entities.Events;

public class AuctionImagesRemoved : AuctionEvent
{
    public List<Guid> ImageIds { get; set; } = [];
}
