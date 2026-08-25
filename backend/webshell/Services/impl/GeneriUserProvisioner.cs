using System.Security.Claims;
using appcore.Infra.Evaluators;

namespace backend.Services;

class GeneriUserProvisioner : IUserProvisioner
{
    public ProvisionUserInput BuildInput(ClaimsPrincipal principal)
    {
        // profile pictures are not standardised. Because of that, we cannot handle it without knowing
        // more details on the specific provider type.
        return ProvisionUserClaims.ReadCore(principal);
    }

    public void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input)
    {
    }
}
