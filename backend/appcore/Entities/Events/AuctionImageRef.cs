using System;

namespace appcore.Entities.Events;

public class AuctionImageRef
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string FilePath { get; set; }
    public required string Hash { get; set; }
}
