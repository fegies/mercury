using System.Text.Json;
using appcore.Entities;
using appcore.Entities.Events;

namespace appcore.Infra;

public static class EventSerializer
{
	private static readonly JsonSerializerOptions SerializeOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private static readonly JsonSerializerOptions DeserializeOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	private static readonly Dictionary<string, Type> TypeMap = new()
	{
		["AuctionCreated"] = typeof(AuctionCreated),
		["AuctionUpdated"] = typeof(AuctionUpdated),
		["AuctionImagesAdded"] = typeof(AuctionImagesAdded),
		["AuctionImagesRemoved"] = typeof(AuctionImagesRemoved),
		["AuctionClosed"] = typeof(AuctionClosed),
		["AuctionCloseExtended"] = typeof(AuctionCloseExtended),
		["AuctionCancelled"] = typeof(AuctionCancelled),
		["BidPlaced"] = typeof(BidPlaced),
		["UserCreated"] = typeof(UserCreated),
		["UserUpdated"] = typeof(UserUpdated),
		["UserRoleChanged"] = typeof(UserRoleChanged),
	};

	private static readonly Dictionary<Type, string> ReverseTypeMap = TypeMap.ToDictionary(kv => kv.Value, kv => kv.Key);

	public static StoredEvent Deserialize(AppEvent row)
	{
		if (!TypeMap.TryGetValue(row.EventType, out var type))
			throw new InvalidOperationException($"Unknown event type: {row.EventType}");
		return (StoredEvent)row.Payload.Deserialize(type, DeserializeOptions)!;
	}

	public static AppEvent Serialize(StoredEvent e)
	{
		if (!ReverseTypeMap.TryGetValue(e.GetType(), out var typeName))
			throw new InvalidOperationException($"Unknown event type: {e.GetType().Name}");
		return new AppEvent
		{
			EventType = typeName,
			Payload = JsonSerializer.SerializeToDocument(e, e.GetType(), SerializeOptions),
		};
	}
}
