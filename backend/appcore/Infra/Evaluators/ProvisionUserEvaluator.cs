using appcore.Entities;
using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public sealed record ProvisionUserInput(
	Guid? ExistingUserId,
	string Issuer,
	string Subject,
	string Name,
	string? Email,
	string? ProfilePictureUrl,
	string? Role
);

public sealed record UserProvisionResult(Guid UserId);

public class ProvisionUserEvaluator : IDecisionFunction<ProvisionUserInput, UserProvisionResult>
{
	public EventSelector InitialSelector(ProvisionUserInput input)
		=> input.ExistingUserId is { } userId
			? EventSelector.ForUser(userId, EventTypeNames.User)
			: EventTypeNames.ForOidIdentity(input.Issuer, input.Subject);

	public DecisionStep<UserProvisionResult> Step(ProvisionUserInput input, EventContext context)
	{
		if (string.IsNullOrEmpty(input.Issuer))
			throw new InvariantViolation("Issuer must be non-empty.");

		if (string.IsNullOrEmpty(input.Subject))
			throw new InvariantViolation("Subject must be non-empty.");

		if (string.IsNullOrEmpty(input.Name))
			throw new InvariantViolation("Name must be non-empty.");

		var state = UserFold.State(context);

		if (state.UserId == Guid.Empty)
		{
			var userId = Guid.NewGuid();
			return new DecisionStep<UserProvisionResult>.Complete(new UserProvisionResult(userId),
			[
				new UserCreated
				{
					UserId = userId,
					OidIss = input.Issuer,
					OidSub = input.Subject,
					Name = input.Name,
					Email = input.Email,
					ProfilePictureUrl = input.ProfilePictureUrl,
					Role = input.Role,
				},
			]);
		}

		var events = new List<StoredEvent>();

		if (input.Name != state.Name || input.Email != state.Email || input.ProfilePictureUrl != state.ProfilePictureUrl)
		{
			events.Add(new UserUpdated
			{
				UserId = state.UserId,
				Name = input.Name != state.Name ? input.Name : null,
				Email = input.Email != state.Email ? input.Email : null,
				ProfilePictureUrl = input.ProfilePictureUrl != state.ProfilePictureUrl ? input.ProfilePictureUrl : null,
			});
		}

		if (input.Role != state.Role)
		{
			events.Add(new UserRoleChanged
			{
				UserId = state.UserId,
				Role = input.Role,
			});
		}

		return new DecisionStep<UserProvisionResult>.Complete(new UserProvisionResult(state.UserId), events);
	}
}
