using appcore.Infra.Live;

namespace appcore.Tests.Infrastructure;

/// <summary>
/// Test convenience mirroring the historical hub.Connect API on top of the
/// capped TryConnect: tests never expect the cap, so failures fail loudly.
/// </summary>
internal static class LiveEventHubTestExtensions
{
	public static LiveConnection Connect(this LiveEventHub hub, Guid userId)
		=> hub.TryConnect(userId, out var connection)
			? connection
			: throw new InvalidOperationException("connection cap reached");
}
