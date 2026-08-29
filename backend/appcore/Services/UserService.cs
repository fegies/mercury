using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;

namespace appcore.Services;

public class UserService(IEventReader reader)
{
	public async Task<UserState?> GetUserById(Guid userId)
	{
		var context = await reader.Read(
			[EventSelector.ForUser(userId, EventTypeNames.User)],
			CancellationToken.None);
		var state = UserFold.State(context);
		return state.UserId == Guid.Empty ? null : state;
	}

	public async Task<Guid?> FindUserIdByOidIdentity(string issuer, string subject, CancellationToken ct)
	{
		var context = await reader.Read(
			[EventTypeNames.ForOidIdentity(issuer, subject)],
			ct);
		return context.Events
			.Select(EventSerializer.Deserialize)
			.OfType<UserCreated>()
			.FirstOrDefault()?.UserId;
	}
}
