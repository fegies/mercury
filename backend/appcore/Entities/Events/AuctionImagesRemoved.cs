using System;
using System.Collections.Generic;

namespace appcore.Entities.Events;

public class AuctionImagesRemoved : StoredEvent
{
    public required Guid AuctionId { get; set; }

    public List<Guid> ImageIds { get; set; } = [];
}
