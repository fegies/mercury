using System.Security.Claims;
using backend.Entities;

namespace backend.Services;

class GeneriUserProvisioner : IUserProvisioner
{
    public Task ProvisionUser(UserEntity user, ClaimsPrincipal principal)
    {
        // profile pictures are not standardised. Because of that, we cannot handle it without knowing
        // more details on the specific provider type.
        return Task.CompletedTask;
    }
}
