using System.Security.Claims;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;

namespace backend.Services;

internal class UserProvisionService(IncomingEventHandler<ProvisionUserInput, UserProvisionResult> handler, IUserProvisioner provisioner, UserService users)
{
    public async Task ProvisionUser(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        var input = await provisioner.BuildInputAsync(principal, accessToken, ct);

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

        provisioner.ApplyClaims(principal, input);
    }
}
