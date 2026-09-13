using appcore.Entities;
using Npgsql;

namespace appcore.Infra;

public static class EventTypeNames
{
	public const string AuctionCreated = nameof(Entities.Events.AuctionCreated);
	public const string AuctionUpdated = nameof(Entities.Events.AuctionUpdated);
	public const string AuctionImagesAdded = nameof(Entities.Events.AuctionImagesAdded);
	public const string AuctionImagesRemoved = nameof(Entities.Events.AuctionImagesRemoved);
	public const string AuctionClosed = nameof(Entities.Events.AuctionClosed);
	public const string AuctionCloseExtended = nameof(Entities.Events.AuctionCloseExtended);
	public const string AuctionCancelled = nameof(Entities.Events.AuctionCancelled);
	public const string BidPlaced = nameof(Entities.Events.BidPlaced);
	public const string UserCreated = nameof(Entities.Events.UserCreated);
	public const string UserUpdated = nameof(Entities.Events.UserUpdated);
	public const string UserRoleChanged = nameof(Entities.Events.UserRoleChanged);

	public static readonly string[] Auction = [AuctionCreated, AuctionUpdated, AuctionImagesAdded, AuctionImagesRemoved, AuctionClosed, AuctionCloseExtended, AuctionCancelled, BidPlaced];
	public static readonly string[] AuctionClosure = [AuctionCreated, AuctionClosed, AuctionCloseExtended, AuctionCancelled];
	public static readonly string[] User = [UserCreated, UserUpdated, UserRoleChanged];

	public static EventSelector ForOidIdentity(string issuer, string subject)
		=> new([UserCreated], [new DimensionConstraint("oidIss", issuer), new DimensionConstraint("oidSub", subject)]);
}

public sealed record DimensionConstraint(string Property, string Value);

public sealed record EventSelector(
	IReadOnlyCollection<string> EventTypes,
	IReadOnlyCollection<DimensionConstraint> Constraints)
{
	public static EventSelector ForAuction(Guid auctionId, params string[] eventTypes)
		=> new(eventTypes, [new DimensionConstraint("auctionId", auctionId.ToString())]);

	public static EventSelector ForUser(Guid userId, params string[] eventTypes)
		=> new(eventTypes, [new DimensionConstraint("userId", userId.ToString())]);

	public static EventSelector OfTypes(params string[] eventTypes)
		=> new(eventTypes, []);
}

public abstract record DecisionStep<TResult>
{
	public sealed record NeedMoreContext(EventSelector AdditionalSelector) : DecisionStep<TResult>;
	public sealed record Complete(TResult Value, IReadOnlyList<StoredEvent> EventsToAppend) : DecisionStep<TResult>;
}

public interface IDecisionFunction<in TInput, TResult>
{
	EventSelector InitialSelector(TInput input);
	DecisionStep<TResult> Step(TInput input, EventContext context);
}

public sealed record EventHandlerOptions
{
	public int MaxAttempts { get; init; } = 10;
	public int MaxExpansions { get; init; } = 10;
}

public sealed class IncomingEventHandler<TInput, TResult>(
	IEventStore store,
	IDecisionFunction<TInput, TResult> decision,
	EventHandlerOptions options)
{
	public async Task<TResult> Execute(TInput input, CancellationToken ct)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				return await TryExecute(input, ct);
			}
			catch (ConcurrencyConflictException ex)
			{
				if (attempt >= options.MaxAttempts - 1)
					throw new InvalidOperationException("Event handler did not converge after repeated attempts.", ex);
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt)), ct);
			}
			catch (NpgsqlException ex) when (ex.IsTransient)
			{
				if (attempt >= options.MaxAttempts - 1)
					throw new InvalidOperationException("Event handler did not converge after repeated attempts.", ex);
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt)), ct);
			}
		}
	}

	private async Task<TResult> TryExecute(TInput input, CancellationToken ct)
	{
		var reader = store.Reader;
		var boundary = ConsistencyBoundary.StartWith(decision.InitialSelector(input), head: 0);
		var context = await reader.Read(boundary.Selectors.ToArray(), ct);
		boundary = boundary with { LastPosition = context.Head };

		for (var expansion = 0; expansion < options.MaxExpansions; expansion++)
		{
			ct.ThrowIfCancellationRequested();

			switch (decision.Step(input, context))
			{
				case DecisionStep<TResult>.NeedMoreContext more:
					boundary = boundary.Include(more.AdditionalSelector);
					context = await reader.Read(boundary.Selectors.ToArray(), ct);
					boundary = boundary with { LastPosition = context.Head };
					continue;

				case DecisionStep<TResult>.Complete done:
					await store.Append(done.EventsToAppend, boundary, ct);
					return done.Value;
			}
		}

		throw new InvalidOperationException("Decision did not converge within the expansion limit.");
	}
}
