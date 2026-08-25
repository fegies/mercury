using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Tests.Infrastructure;
using Xunit;

namespace appcore.Tests.Evaluators;

public class ProvisionUserEvaluatorTests
{
	private static ProvisionUserInput ValidInput(
		Guid? existingUserId = null,
		string? issuer = null,
		string? subject = null,
		string? name = null,
		string? email = "user@example.org",
		string? picture = null,
		string? role = null)
		=> new(existingUserId, issuer ?? "https://idp.example.org", subject ?? "sub-1", name ?? "Jane Doe", email, picture, role);

	private static UserCreated ExistingUser(
		Guid? userId = null,
		string issuer = "https://idp.example.org",
		string subject = "sub-1",
		string name = "Jane Doe",
		string? email = "user@example.org",
		string? picture = null,
		string? role = null)
		=> new()
		{
			UserId = userId ?? Guid.NewGuid(),
			OidIss = issuer,
			OidSub = subject,
			Name = name,
			Email = email,
			ProfilePictureUrl = picture,
			Role = role,
		};

	private static EventContext ContextOf(params StoredEvent[] events)
		=> TestContext.From(events.Select(EventSerializer.Serialize).ToArray());

	[Fact]
	public void Step_NewIdentity_EmitsUserCreatedWithNewId()
	{
		var input = ValidInput(email: "jane@example.org", picture: "https://pic", role: "Admin");
		var evaluator = new ProvisionUserEvaluator();

		var step = evaluator.Step(input, TestContext.From());

		var result = Assert.IsType<DecisionStep<UserProvisionResult>.Complete>(step);
		var created = Assert.IsType<UserCreated>(Assert.Single(result.EventsToAppend));
		Assert.Equal(result.Value.UserId, created.UserId);
		Assert.Equal(input.Issuer, created.OidIss);
		Assert.Equal(input.Subject, created.OidSub);
		Assert.Equal("Jane Doe", created.Name);
		Assert.Equal("jane@example.org", created.Email);
		Assert.Equal("https://pic", created.ProfilePictureUrl);
		Assert.Equal("Admin", created.Role);
	}

	[Fact]
	public void Step_ExistingIdentityUnchanged_AppendsNothing()
	{
		var existing = ExistingUser();
		var input = ValidInput(issuer: existing.OidIss, subject: existing.OidSub, name: existing.Name, email: existing.Email, picture: existing.ProfilePictureUrl, role: existing.Role);
		var evaluator = new ProvisionUserEvaluator();

		var step = evaluator.Step(input, ContextOf(existing));

		var result = Assert.IsType<DecisionStep<UserProvisionResult>.Complete>(step);
		Assert.Equal(existing.UserId, result.Value.UserId);
		Assert.Empty(result.EventsToAppend);
	}

	[Fact]
	public void Step_ChangedNameAndEmail_EmitsSingleUserUpdatedWithDeltas()
	{
		var existing = ExistingUser();
		var input = ValidInput(issuer: existing.OidIss, subject: existing.OidSub, name: "Jane Smith", email: "jane.smith@example.org", picture: existing.ProfilePictureUrl, role: existing.Role);
		var evaluator = new ProvisionUserEvaluator();

		var step = evaluator.Step(input, ContextOf(existing));

		var result = Assert.IsType<DecisionStep<UserProvisionResult>.Complete>(step);
		var updated = Assert.IsType<UserUpdated>(Assert.Single(result.EventsToAppend));
		Assert.Equal(existing.UserId, updated.UserId);
		Assert.Equal("Jane Smith", updated.Name);
		Assert.Equal("jane.smith@example.org", updated.Email);
		Assert.Null(updated.ProfilePictureUrl);
	}

	[Fact]
	public void Step_ChangedRole_EmitsUserRoleChanged()
	{
		var existing = ExistingUser(role: "Admin");
		var input = ValidInput(issuer: existing.OidIss, subject: existing.OidSub, name: existing.Name, email: existing.Email, picture: existing.ProfilePictureUrl, role: null);
		var evaluator = new ProvisionUserEvaluator();

		var step = evaluator.Step(input, ContextOf(existing));

		var result = Assert.IsType<DecisionStep<UserProvisionResult>.Complete>(step);
		var roleChanged = Assert.IsType<UserRoleChanged>(Assert.Single(result.EventsToAppend));
		Assert.Equal(existing.UserId, roleChanged.UserId);
		Assert.Null(roleChanged.Role);
	}

	[Fact]
	public void Step_ReplayAfterUpdates_ComputesDeltasAgainstFoldedState()
	{
		var existing = ExistingUser(name: "Jane Doe");
		var renamed = new UserUpdated { UserId = existing.UserId, Name = "Jane Smith" };
		var input = ValidInput(issuer: existing.OidIss, subject: existing.OidSub, name: "Jane Smith", email: existing.Email);
		var evaluator = new ProvisionUserEvaluator();

		var step = evaluator.Step(input, ContextOf(existing, renamed));

		var result = Assert.IsType<DecisionStep<UserProvisionResult>.Complete>(step);
		Assert.Empty(result.EventsToAppend);
	}

	[Fact]
	public void InitialSelector_UnknownIdentity_MatchesOnlyUserCreatedOfGivenIdentity()
	{
		var input = ValidInput();
		var evaluator = new ProvisionUserEvaluator();

		var selector = evaluator.InitialSelector(input);

		var matching = new UserCreated { UserId = Guid.NewGuid(), OidIss = input.Issuer, OidSub = input.Subject, Name = "Jane" };
		var otherSubject = new UserCreated { UserId = Guid.NewGuid(), OidIss = input.Issuer, OidSub = "other-sub", Name = "Other" };
		var update = new UserUpdated { UserId = matching.UserId, Name = "Jane Smith" };
		Assert.True(EventPayload.Matches(EventSerializer.Serialize(matching), [selector]));
		Assert.False(EventPayload.Matches(EventSerializer.Serialize(otherSubject), [selector]));
		Assert.False(EventPayload.Matches(EventSerializer.Serialize(update), [selector]));
	}

	[Fact]
	public void InitialSelector_KnownUserId_MatchesAllEventsOfThatUser()
	{
		var userId = Guid.NewGuid();
		var input = ValidInput(existingUserId: userId);
		var evaluator = new ProvisionUserEvaluator();

		var selector = evaluator.InitialSelector(input);

		var created = new UserCreated { UserId = userId, OidIss = "https://other-idp.example.org", OidSub = "sub-9", Name = "Jane" };
		var update = new UserUpdated { UserId = userId, Name = "Jane Smith" };
		var otherUser = new UserUpdated { UserId = Guid.NewGuid(), Name = "Other" };
		Assert.True(EventPayload.Matches(EventSerializer.Serialize(created), [selector]));
		Assert.True(EventPayload.Matches(EventSerializer.Serialize(update), [selector]));
		Assert.False(EventPayload.Matches(EventSerializer.Serialize(otherUser), [selector]));
	}

	[Fact]
	public void Step_MissingIssuer_ThrowsInvariantViolation()
	{
		var input = new ProvisionUserInput(null, null!, "sub-1", "Jane Doe", null, null, null);
		var evaluator = new ProvisionUserEvaluator();

		Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));
	}

	[Fact]
	public void Step_MissingSubject_ThrowsInvariantViolation()
	{
		var input = new ProvisionUserInput(null, "https://idp.example.org", null!, "Jane Doe", null, null, null);
		var evaluator = new ProvisionUserEvaluator();

		Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));
	}

	[Fact]
	public void Step_EmptyName_ThrowsInvariantViolation()
	{
		var input = ValidInput(name: "");
		var evaluator = new ProvisionUserEvaluator();

		var ex = Assert.Throws<InvariantViolation>(() => evaluator.Step(input, TestContext.From()));

		Assert.Equal("Name must be non-empty.", ex.Message);
	}
}
