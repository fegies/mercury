using System.Security.Claims;
using appcore.Infra.Evaluators;

namespace backend.Services;

class GeneriUserProvisioner : IUserProvisioner
{
    public Task<ProvisionUserInput> BuildInputAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        // profile pictures are not standardised. Because of that, we cannot handle it without knowing
        // more details on the specific provider type.
        return Task.FromResult(ProvisionUserClaims.ReadCore(principal));
    }

    public void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input)
    {
    }
}
