using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Events;
using appcore.Infra.Evaluators;
using appcore.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace appcore.Infra.Expiry;

/// <summary>
/// Keeps the in-process deadline scheduler in sync with the stored auction log and turns due
/// deadlines into <see cref="AuctionClosed"/> appends with <c>Reason = Expired</c>. Subscribes to
/// the lifecycle events that change an auction's closure, reconciles open auctions at startup
/// (so long-running deadlines survive restarts, bounded by the scheduler's 24h cap), swallows
/// benign conflicts when a close races a manual close or cancellation, and re-arms the deadline
/// when a close fails to converge (a lost deadline entry would otherwise leave the auction open
/// until the next restart).
/// </summary>
public sealed class AuctionExpiryWorker : BackgroundService
{
	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

	private readonly IAppBus _bus;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly AuctionDeadlineScheduler _scheduler;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<AuctionExpiryWorker> _logger;
	private readonly List<IDisposable> _subscriptions = [];

	public AuctionExpiryWorker(
		IAppBus bus,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider,
		ILogger<AuctionExpiryWorker> logger)
	{
		_bus = bus;
		_scopeFactory = scopeFactory;
		_scheduler = new AuctionDeadlineScheduler(timeProvider);
		_timeProvider = timeProvider;
		_logger = logger;
	}

	public override async Task StartAsync(CancellationToken cancellationToken)
	{
		// Subscribe before reconciling: events published between the two get scheduled by their
		// subscriptions, and the reconciliation pass covers anything already on disk. Scheduling
		// is idempotent (latest deadline wins), so overlap is harmless.
		_subscriptions.Add(_bus.Subscribe<AuctionCreated>(OnAuctionCreated));
		_subscriptions.Add(_bus.Subscribe<AuctionCloseExtended>(OnAuctionCloseExtended));
		_subscriptions.Add(_bus.Subscribe<AuctionClosed>(OnAuctionClosed));
		_subscriptions.Add(_bus.Subscribe<AuctionCancelled>(OnAuctionCancelled));
		_subscriptions.Add(_bus.Subscribe<AuctionExpiryElapsed>(OnExpiryElapsed));

		await ReconcileOpenAuctions(cancellationToken);
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
		await foreach (var due in _scheduler.ElapsedAsync(stoppingToken))
			await _bus.EmitAsync(due, stoppingToken);
	}

	private ValueTask OnAuctionCreated(AuctionCreated created, CancellationToken ct)
	{
		_scheduler.Schedule(created.AuctionId, created.ClosureTime);
		return ValueTask.CompletedTask;
	}

	private ValueTask OnAuctionCloseExtended(AuctionCloseExtended extended, CancellationToken ct)
	{
		_scheduler.Schedule(extended.AuctionId, extended.NewClosureTime);
		return ValueTask.CompletedTask;
	}

	private ValueTask OnAuctionClosed(AuctionClosed closed, CancellationToken ct)
	{
		_scheduler.Remove(closed.AuctionId);
		return ValueTask.CompletedTask;
	}

	private ValueTask OnAuctionCancelled(AuctionCancelled cancelled, CancellationToken ct)
	{
		_scheduler.Remove(cancelled.AuctionId);
		return ValueTask.CompletedTask;
	}

	private async ValueTask OnExpiryElapsed(AuctionExpiryElapsed elapsed, CancellationToken ct)
	{
		using var scope = _scopeFactory.CreateScope();
		var handler = scope.ServiceProvider.GetRequiredService<IncomingEventHandler<AuctionClosed, bool>>();
		var close = new AuctionClosed
		{
			AuctionId = elapsed.AuctionId,
			Reason = AuctionCloseReason.Expired,
		};

		try
		{
			await handler.Execute(close, ct);
			_logger.AutoClosedAuction(elapsed.AuctionId);
		}
		catch (InvariantViolation ex)
		{
			_logger.AutoCloseNotApplied(elapsed.AuctionId, ex.Message);
		}
		catch (ConvergenceException ex)
		{
			// The scheduler entry was consumed before the close ran; without a re-arm the
			// close is lost and the auction stays open (still accepting bids) until a
			// restart. Retry a bounded distance out instead of hot-looping.
			_logger.AutoCloseFailed(elapsed.AuctionId, ex);
			_scheduler.Schedule(elapsed.AuctionId, _timeProvider.GetUtcNow().UtcDateTime + RetryDelay);
		}
	}

	private async Task ReconcileOpenAuctions(CancellationToken ct)
	{
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var reader = scope.ServiceProvider.GetRequiredService<IEventReader>();
			// Only the lifecycle events that move the closing time matter here: create
			// sets it, close/cancel retire the auction, extend pushes it out.
			var context = await reader.Read([EventSelector.OfTypes(EventTypeNames.AuctionClosure)], ct);

			var states = context.Fold(new Dictionary<Guid, AuctionState>(), (acc, e) =>
			{
				if (e is not AuctionEvent auction)
					return acc;
				acc[auction.AuctionId] = AuctionState.Incorporate(acc.GetValueOrDefault(auction.AuctionId, AuctionState.Empty), e);
				return acc;
			});

			var open = 0;
			foreach (var (auctionId, state) in states)
			{
				if (state.IsClosed || state.AuctionId == Guid.Empty)
					continue;
				_scheduler.Schedule(auctionId, state.ClosureTime);
				open++;
			}

			_logger.ReconciledOpenAuctions(open);
		}
		catch (Exception ex)
		{
			// Startup reconciliation is best-effort; a failure must not bring the worker down.
			_logger.StartupReconciliationFailed(ex);
		}
	}
}