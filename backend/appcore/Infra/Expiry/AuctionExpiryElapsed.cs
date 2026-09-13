namespace appcore.Infra.Expiry;

/// <summary>
/// Bus-internal signal raised once an open auction's closure deadline is reached.
/// <c>Deadline</c> records the exact scheduled instant so receivers can correlate it
/// with the auction's stored events. Never persisted; derived purely from scheduling.
/// </summary>
public record AuctionExpiryElapsed(Guid AuctionId, DateTime Deadline);