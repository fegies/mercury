using System;
using System.Security.Claims;
using appcore.Infra.Evaluators;

namespace backend.Services;

internal static class ProvisionUserClaims
{
    public static ProvisionUserInput ReadCore(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst("sub") ?? throw new Exception("sub not found");
        var name = principal.FindFirst("name") ?? throw new Exception("name not found");
        var email = principal.FindFirst("email") ?? throw new Exception("email not found");

        return new ProvisionUserInput(null, sub.Issuer, sub.Value, name.Value, email.Value, null, null);
    }
}

interface IUserProvisioner
{
    /// <summary>
    /// Build the provisioning command for this provider, enriching the core claims
    /// (sub/name/email) with provider-specific profile and role information.
    /// </summary>
    ProvisionUserInput BuildInput(ClaimsPrincipal principal);

    /// <summary>
    /// Stamp provider-specific claims (e.g. mercury.role) onto the authenticated principal.
    /// </summary>
    void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input);
}
