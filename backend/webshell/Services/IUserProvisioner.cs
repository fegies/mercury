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
    Task<ProvisionUserInput> BuildInputAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct);

    /// <summary>
    /// Stamp provider-specific claims (e.g. mercury.role) onto the authenticated principal.
    /// </summary>
    void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input);

    /// <summary>
    /// Discard resources the provisioner staged while building the input
    /// (e.g. an Entra profile-photo blob) when the provisioning events
    /// could not be appended. Default: nothing was staged.
    /// </summary>
    Task RollbackPendingWritesAsync(CancellationToken ct) => Task.CompletedTask;
}
