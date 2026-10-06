using System.Security.Claims;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;

namespace backend.Services;

internal class UserProvisionService(IncomingEventHandler<ProvisionUserInput, UserProvisionResult> handler, IUserProvisioner provisioner, UserService users)
{
    public Task ProvisionUser(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
        => ProvisionUser(principal, accessToken, preserveProfilePicture: false, ct);

    public async Task ProvisionUser(ClaimsPrincipal principal, string? accessToken, bool preserveProfilePicture, CancellationToken ct)
    {
        var built = await provisioner.BuildInputAsync(principal, accessToken, ct);
        var input = preserveProfilePicture ? built with { PreserveProfilePicture = true } : built;

        var existingUserId = await users.FindUserIdByOidIdentity(input.Issuer, input.Subject, ct);
        if (existingUserId.HasValue)
            input = input with { ExistingUserId = existingUserId };

        var result = await handler.Execute(input, CancellationToken.None);

        // ensure that the local userid claim is not present
        var previous_id_claims = principal.FindAll("local_userid").ToList();
        if (previous_id_claims.Count > 0)
            foreach (var identity in principal.Identities)
                foreach (var claim in previous_id_claims)
                    identity.TryRemoveClaim(claim);

        principal.Identities.First().AddClaim(new Claim("local_userid", result.UserId.ToString()));

        // session refreshes re-run provisioning on a principal that already
        // carries a role stamp; clear it so ApplyClaims re-stamps exactly once.
        foreach (var identity in principal.Identities)
            foreach (var role in identity.FindAll("mercury.role").ToList())
                identity.TryRemoveClaim(role);

        provisioner.ApplyClaims(principal, input);
    }
}
