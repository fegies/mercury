using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Services;

public class UserProvisionFlowTests
{
	private static ProvisionUserInput Input(
		Guid? existingUserId = null,
		string subject = "sub-1",
		string name = "Jane Doe",
		string? email = "jane@example.org",
		string? picture = null,
		string? role = null)
		=> new(existingUserId, "https://idp.example.org", subject, name, email, picture, role);

	[Fact]
	public async Task Execute_RepeatedLogin_ConvergesToSingleUserWithoutNewEvents()
	{
		var store = new InMemoryEventStore();
		var handler = new IncomingEventHandler<ProvisionUserInput, UserProvisionResult>(store, new ProvisionUserEvaluator(), new EventHandlerOptions());

		var first = await handler.Execute(Input(), CancellationToken.None);
		var second = await handler.Execute(Input(name: "Jane Doe", email: "jane@example.org"), CancellationToken.None);

		Assert.Equal(first.UserId, second.UserId);
		Assert.Single(store.Events);
	}

	[Fact]
	public async Task Execute_ChangedProfile_AppendsUserUpdatedAndReplaysToSameState()
	{
		var store = new InMemoryEventStore();
		var users = new UserService(store);
		var handler = new IncomingEventHandler<ProvisionUserInput, UserProvisionResult>(store, new ProvisionUserEvaluator(), new EventHandlerOptions());

		var first = await handler.Execute(Input(), CancellationToken.None);
		var hint = await users.FindUserIdByOidIdentity("https://idp.example.org", "sub-1", CancellationToken.None);
		Assert.Equal(first.UserId, hint);

		var result = await handler.Execute(Input(existingUserId: hint, name: "Jane Smith", picture: "https://pic"), CancellationToken.None);
		Assert.Equal(first.UserId, result.UserId);

		Assert.Equal(2, store.Events.Count);
		Assert.Equal(EventTypeNames.UserUpdated, store.Events[1].EventType);

		var state = await users.GetUserById(first.UserId);
		Assert.NotNull(state);
		Assert.Equal("Jane Smith", state.Name);
		Assert.Equal("https://pic", state.ProfilePictureUrl);
	}

	[Fact]
	public async Task Execute_ConcurrentFirstLogin_RetriesConflictAndAdoptsWinner()
	{
		var inner = new InMemoryEventStore();
		var winnerId = Guid.NewGuid();
		var store = new RaceSimulatingStore(inner, winnerId);
		var handler = new IncomingEventHandler<ProvisionUserInput, UserProvisionResult>(store, new ProvisionUserEvaluator(), new EventHandlerOptions());

		var result = await handler.Execute(Input(subject: "sub-1", name: "Jane Doe", email: "jane@example.org"), CancellationToken.None);

		Assert.Equal(winnerId, result.UserId);
		Assert.Single(inner.Events);
		Assert.Equal(EventTypeNames.UserCreated, inner.Events[0].EventType);
	}

	private sealed class RaceSimulatingStore(InMemoryEventStore inner, Guid winnerId) : IEventStore
	{
		public bool Injected { get; set; }

		public IEventReader Reader => inner;

		public Task Append(IReadOnlyList<StoredEvent> events, ConsistencyBoundary boundary, CancellationToken ct)
		{
			if (!Injected && events.OfType<UserCreated>().Any())
			{
				Injected = true;
				inner.Seed([EventSerializer.Serialize(new UserCreated
				{
					UserId = winnerId,
					OidIss = "https://idp.example.org",
					OidSub = "sub-1",
					Name = "Jane Doe",
					Email = "jane@example.org",
				}) with { SequenceId = (inner.Events.Count > 0 ? inner.Events.Max(e => e.SequenceId) : 0) + 1 }]);
			}
			return inner.Append(events, boundary, ct);
		}
	}
}
