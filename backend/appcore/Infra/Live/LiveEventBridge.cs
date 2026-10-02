using appcore.Configuration;
using appcore.Entities.Events;
using appcore.Infra.Events;
using appcore.Infra.Evaluators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace appcore.Infra.Live;

/// <summary>
/// Turns committed domain events into live SSE traffic. Broadcasts an
/// auction-updated ping on every relevant event and pushes targeted
/// notifications — outbid, won, cancelled — to the affected users' open
/// connections. Reads nothing while no client is connected; the per-event
/// facts it needs (previous leader, bidders, title) are reconstructed from
/// the event store on demand, so there is no in-memory mirror to maintain
/// and nothing to reconcile at startup.
/// </summary>
public sealed class LiveEventBridge(
	IAppBus bus,
	LiveEventHub hub,
	IServiceScopeFactory scopeFactory,
	AuctionConfig auctionConfig) : BackgroundService
{
	private readonly List<IDisposable> _subscriptions = [];

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		// Subscribing happens before ExecuteAsync parks forever; events arriving
		// during startup dispatch straight into the handlers below.
		_subscriptions.Add(bus.Subscribe<BidPlaced>(OnBidPlaced));
		_subscriptions.Add(bus.Subscribe<AuctionClosed>(OnAuctionClosed));
		_subscriptions.Add(bus.Subscribe<AuctionCancelled>(OnAuctionCancelled));
		_subscriptions.Add(bus.Subscribe<AuctionCloseExtended>(OnAuctionCloseExtended));

		await base.StartAsync(cancellationToken);
	}

	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken);
		foreach (var subscription in _subscriptions)
			subscription.Dispose();
		_subscriptions.Clear();
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await Task.Delay(Timeout.Infinite, stoppingToken);
	}

	private async ValueTask OnBidPlaced(BidPlaced bid, CancellationToken ct)
	{
		if (!hub.HasConnections)
			return;

		hub.Broadcast(LiveEvent.AuctionUpdated(bid.AuctionId, LiveEventType.BidPlaced));

		var context = await ReadAuction(bid.AuctionId, ct);
		var reconstruction = OutbidDetection.Reconstruct(context, bid);
		if (reconstruction is null || OutbidDetection.OutbidUser(reconstruction, bid.BidderId) is not { } outbidUser)
			return;

		var (_, price) = BidPricing.Compute(reconstruction.Current, auctionConfig.MinBidIncrement);
		hub.PushToUser(outbidUser, LiveEvent.Notification(LiveEventType.Outbid, bid.AuctionId, reconstruction.Current.Title, price));
	}

	private async ValueTask OnAuctionClosed(AuctionClosed closed, CancellationToken ct)
	{
		if (!hub.HasConnections)
			return;

		hub.Broadcast(LiveEvent.AuctionUpdated(closed.AuctionId, LiveEventType.Closed));

		if (closed.WinnerUserId is not { } winner)
			return;

		var context = await ReadAuction(closed.AuctionId, ct);
		hub.PushToUser(winner, LiveEvent.Notification(LiveEventType.Won, closed.AuctionId, AuctionFold.State(context).Title, closed.WinningPrice));
	}

	private async ValueTask OnAuctionCancelled(AuctionCancelled cancelled, CancellationToken ct)
	{
		if (!hub.HasConnections)
			return;

		hub.Broadcast(LiveEvent.AuctionUpdated(cancelled.AuctionId, LiveEventType.Cancelled));

		var context = await ReadAuction(cancelled.AuctionId, ct);
		var title = AuctionFold.State(context).Title;
		foreach (var bidder in OutbidDetection.DistinctBidders(context))
			hub.PushToUser(bidder, LiveEvent.Notification(LiveEventType.Cancelled, cancelled.AuctionId, title, null));
	}

	private ValueTask OnAuctionCloseExtended(AuctionCloseExtended extended, CancellationToken ct)
	{
		if (!hub.HasConnections)
			return ValueTask.CompletedTask;

		hub.Broadcast(LiveEvent.AuctionUpdated(extended.AuctionId, LiveEventType.Extended));
		return ValueTask.CompletedTask;
	}

	private async Task<EventContext> ReadAuction(Guid auctionId, CancellationToken ct)
	{
		using var scope = scopeFactory.CreateScope();
		var reader = scope.ServiceProvider.GetRequiredService<IEventReader>();
		return await reader.Read([EventSelector.ForAuction(auctionId, EventTypeNames.Auction)], ct);
	}
}
