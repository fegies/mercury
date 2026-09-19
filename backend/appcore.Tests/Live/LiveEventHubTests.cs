using appcore.Infra.Live;
using Xunit;

namespace appcore.Tests.Live;

public class LiveEventHubTests
{
	private static LiveEvent Ping(Guid auctionId) => LiveEvent.AuctionUpdated(auctionId, LiveEventType.BidPlaced);

	[Fact]
	public void NewHubHasNoConnections()
	{
		Assert.False(new LiveEventHub().HasConnections);
	}

	[Fact]
	public async Task ConnectOpensAQueueThatReceivesPushes()
	{
		var hub = new LiveEventHub();
		var userId = Guid.NewGuid();
		using var connection = hub.Connect(userId);

		Assert.True(hub.HasConnections);

		hub.PushToUser(userId, Ping(Guid.NewGuid()));

		Assert.True(await connection.Reader.WaitToReadAsync());
		Assert.True(connection.Reader.TryRead(out var received));
		Assert.Equal(LiveEventKind.AuctionUpdated, received.Kind);
	}

	[Fact]
	public void PushToUserIsolatesUsers()
	{
		var hub = new LiveEventHub();
		var alice = Guid.NewGuid();
		var bob = Guid.NewGuid();
		using var aliceConnection = hub.Connect(alice);
		using var bobConnection = hub.Connect(bob);

		hub.PushToUser(bob, Ping(Guid.NewGuid()));

		Assert.False(aliceConnection.Reader.TryRead(out _));
		Assert.True(bobConnection.Reader.TryRead(out _));
	}

	[Fact]
	public void BroadcastReachesEveryConnectionOfEveryUser()
	{
		var hub = new LiveEventHub();
		var alice = Guid.NewGuid();
		var bob = Guid.NewGuid();
		using var firstAlice = hub.Connect(alice);
		using var secondAlice = hub.Connect(alice);
		using var bobConnection = hub.Connect(bob);

		var message = Ping(Guid.NewGuid());
		hub.Broadcast(message);

		Assert.True(firstAlice.Reader.TryRead(out var first));
		Assert.True(secondAlice.Reader.TryRead(out var second));
		Assert.True(bobConnection.Reader.TryRead(out var third));
		Assert.Same(message, first);
		Assert.Same(message, second);
		Assert.Same(message, third);
	}

	[Fact]
	public async Task DisconnectCompletesTheReaderAndStopsDelivery()
	{
		var hub = new LiveEventHub();
		var userId = Guid.NewGuid();
		var connection = hub.Connect(userId);

		connection.Dispose();
		Assert.False(hub.HasConnections);

		// Buffered items still drain, then the reader completes; delivery after
		// disposal is dropped silently.
		hub.PushToUser(userId, Ping(Guid.NewGuid()));

		var drained = await connection.Reader.WaitToReadAsync();
		Assert.False(drained);
		Assert.False(connection.Reader.TryRead(out _));
	}

	[Fact]
	public void DisposeIsIdempotentAndMultipleConnectionsAreIndependent()
	{
		var hub = new LiveEventHub();
		var userId = Guid.NewGuid();
		var first = hub.Connect(userId);
		var second = hub.Connect(userId);

		first.Dispose();
		first.Dispose();
		Assert.True(hub.HasConnections);

		second.Dispose();
		Assert.False(hub.HasConnections);
	}

	[Fact]
	public void PushToUnknownUserIsDroppedSilently()
	{
		var hub = new LiveEventHub();
		hub.PushToUser(Guid.NewGuid(), Ping(Guid.NewGuid()));
		hub.Broadcast(Ping(Guid.NewGuid()));
		Assert.False(hub.HasConnections);
	}
}
