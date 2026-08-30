using appcore.Entities;
using appcore.Entities.Events;

namespace appcore.Infra.Evaluators;

public record UserState(
	Guid UserId,
	string OidIss,
	string OidSub,
	string Name,
	string? Email,
	string? ProfilePictureUrl,
	string? Role
)
{
	public static UserState Incorporate(UserState state, long sequenceId, StoredEvent e) => e switch
	{
		UserCreated created => state with
		{
			UserId = created.UserId,
			OidIss = created.OidIss,
			OidSub = created.OidSub,
			Name = created.Name,
			Email = created.Email,
			ProfilePictureUrl = created.ProfilePictureUrl,
			Role = created.Role,
		},
		UserUpdated updated => state with
		{
			Name = updated.Name ?? state.Name,
			Email = updated.Email ?? state.Email,
			ProfilePictureUrl = updated.ProfilePictureUrl ?? state.ProfilePictureUrl,
		},
		UserRoleChanged roleChanged => state with
		{
			Role = roleChanged.Role,
		},
		_ => state,
	};

	public static readonly UserState Empty = new(
		UserId: Guid.Empty,
		OidIss: "",
		OidSub: "",
		Name: "",
		Email: null,
		ProfilePictureUrl: null,
		Role: null
	);
}

public static class UserFold
{
	public static UserState State(EventContext context)
		=> context.Fold(UserState.Empty, UserState.Incorporate);
}
