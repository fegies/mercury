using System.Text.Json.Serialization;

namespace appcore.Infra.Live;

/// <summary>
/// What a live event is for: a targeted user notification (a toast) or a
/// broadcast ping telling clients that some auction changed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LiveEventKind
{
	Notification,
	AuctionUpdated,
}

/// <summary>
/// The triggering domain event: the toast type for notifications, the kind of
/// change for broadcast pings.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LiveEventType
{
	Outbid,
	Won,
	Cancelled,
	BidPlaced,
	Closed,
	Extended,
}

/// <summary>
/// A single frame pushed over the live SSE stream. Notifications are targeted
/// at one user; auction updates are broadcast to every connected client, whose
/// pages refetch the affected auction. Transient by design: nothing is stored.
/// </summary>
public sealed record LiveEvent(
	LiveEventKind Kind,
	LiveEventType Type,
	Guid AuctionId,
	string? Title,
	decimal? Price)
{
	public static LiveEvent Notification(LiveEventType type, Guid auctionId, string? title, decimal? price)
		=> new(LiveEventKind.Notification, type, auctionId, title, price);

	public static LiveEvent AuctionUpdated(Guid auctionId, LiveEventType type)
		=> new(LiveEventKind.AuctionUpdated, type, auctionId, null, null);
}
