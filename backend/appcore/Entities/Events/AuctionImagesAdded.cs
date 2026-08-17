using System;
using System.Collections.Generic;

namespace appcore.Entities.Events;

public class AuctionImagesAdded : StoredEvent
{
    public required Guid AuctionId { get; set; }

    public List<AuctionImageRef> Images { get; set; } = [];
}
