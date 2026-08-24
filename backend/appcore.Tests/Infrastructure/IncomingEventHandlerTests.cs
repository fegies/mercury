using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using Xunit;

namespace appcore.Tests.Infrastructure;

public class IncomingEventHandlerTests
{
	private static AuctionCreated Created(Guid auctionId) => new()
	{
		AuctionId = auctionId,
		Title = "Test Auction",
		Description = "A valid auction",
		MinimumPrice = 10.00m,
		ClosureTime = DateTime.UtcNow.AddDays(7)
	};

	private static AppEvent Event(StoredEvent e, long sequenceId)
		=> EventSerializer.Serialize(e) with { SequenceId = sequenceId };

	private sealed class EchoDecision : IDecisionFunction<AuctionClosed, bool>
	{
		public EventSelector InitialSelector(AuctionClosed input)
			=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction);

		public DecisionStep<bool> Step(AuctionClosed input, EventContext context)
			=> new DecisionStep<bool>.Complete(true, [input]);
	}

	private sealed class ConflictInjectingStore(InMemoryEventStore inner) : IEventStore
	{
		public bool InjectOnce { get; set; } = true;

		public IEventReader Reader => inner;

		public Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
		{
			if (InjectOnce)
			{
				InjectOnce = false;
				var head = inner.Events.Count > 0 ? inner.Events.Max(e => e.SequenceId) : 0;
				var auctionId = ((AuctionClosed)events[0]).AuctionId;
				inner.Seed([Event(new AuctionClosed { AuctionId = auctionId, Reason = AuctionCloseReason.Manual }, head + 1)]);
			}
			return inner.Append(events, boundary, ct);
		}
	}

	private sealed class ExpandingDecision : IDecisionFunction<AuctionCreated, Guid>
	{
		public EventSelector InitialSelector(AuctionCreated input)
			=> EventSelector.ForAuction(input.AuctionId, EventTypeNames.AuctionUpdated);

		public DecisionStep<Guid> Step(AuctionCreated input, EventContext context)
		{
			if (context.Events.Count == 0)
				return new DecisionStep<Guid>.NeedMoreContext(EventSelector.ForAuction(input.AuctionId, EventTypeNames.Auction));
			return new DecisionStep<Guid>.Complete(input.AuctionId, [input]);
		}
	}

	[Fact]
	public async Task Execute_RetriesOnConflictAndConvergesWithFreshContext()
	{
		var auctionId = Guid.NewGuid();
		var store = new InMemoryEventStore();
		var handler = new IncomingEventHandler<AuctionClosed, bool>(
			new ConflictInjectingStore(store),
			new EchoDecision(),
			new EventHandlerOptions());
		var input = new AuctionClosed { AuctionId = auctionId, Reason = AuctionCloseReason.Manual };

		var result = await handler.Execute(input, CancellationToken.None);

		Assert.True(result);
		Assert.Equal(2, store.Events.Count);
	}

	[Fact]
	public async Task Execute_ExpandsScopeUntilDecisionCompletes()
	{
		var auctionId = Guid.NewGuid();
		var store = new InMemoryEventStore([Event(Created(auctionId), 1)]);
		var handler = new IncomingEventHandler<AuctionCreated, Guid>(
			store,
			new ExpandingDecision(),
			new EventHandlerOptions());
		var input = Created(auctionId);

		var result = await handler.Execute(input, CancellationToken.None);

		Assert.Equal(auctionId, result);
		Assert.Equal(2, store.Events.Count);
	}
}
