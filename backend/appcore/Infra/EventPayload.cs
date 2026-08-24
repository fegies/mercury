using System.Text.Json;
using appcore.Entities;

namespace appcore.Infra;

public static class EventPayload
{
	public static void Validate(AppEvent row)
	{
		var root = row.Payload.RootElement;
		if (root.ValueKind != JsonValueKind.Object)
			throw new InvalidOperationException($"Event {row.EventType} must serialize to a JSON object.");

		if (ScalarDimensionCount(root) == 0)
			throw new InvalidOperationException($"Event {row.EventType} must carry at least one top-level scalar property.");
	}

	public static int ScalarDimensionCount(JsonElement root)
	{
		var count = 0;
		foreach (var property in root.EnumerateObject())
		{
			if (IsScalar(property.Value))
				count++;
		}
		return count;
	}

	public static IEnumerable<(string Property, string Value)> ExtractScalarDimensions(JsonElement root)
	{
		foreach (var property in root.EnumerateObject())
		{
			if (IsScalar(property.Value))
				yield return (property.Name, ScalarText(property.Value)!);
		}
	}

	public static bool Matches(AppEvent e, EventSelector[] boundary)
	{
		foreach (var selector in boundary)
		{
			if (!selector.EventTypes.Contains(e.EventType))
				continue;

			var allMatch = true;
			foreach (var constraint in selector.Constraints)
			{
				if (!e.Payload.RootElement.TryGetProperty(constraint.Property, out var prop)
					|| ScalarText(prop) != constraint.Value)
				{
					allMatch = false;
					break;
				}
			}
			if (allMatch)
				return true;
		}
		return false;
	}

	private static bool IsScalar(JsonElement element) => element.ValueKind
		is JsonValueKind.String
		or JsonValueKind.Number
		or JsonValueKind.True
		or JsonValueKind.False;

	private static string? ScalarText(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.String => element.GetString(),
		JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.GetRawText(),
		_ => null,
	};
}
