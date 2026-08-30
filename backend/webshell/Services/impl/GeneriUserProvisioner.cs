using System.Security.Claims;
using System.Text.Json;
using appcore.Infra.Evaluators;

namespace backend.Services;

class GeneriUserProvisioner : IUserProvisioner
{
    public Task<ProvisionUserInput> BuildInputAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        // profile pictures are not standardised. Because of that, we cannot handle it without knowing
        // more details on the specific provider type.
        var is_admin = false;
        var roles_claim = principal.FindFirstValue("roles");
        if (roles_claim != null)
        {
            string[]? roles;
            try
            {
                roles = JsonSerializer.Deserialize<string[]>(roles_claim);
            }
            catch (JsonException)
            {
                // OIDC handlers flatten array claims into a space-delimited
                // string (e.g. "role.admin"), so fall back to splitting.
                roles = roles_claim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            }
            if (roles?.Contains("role.admin") == true)
                is_admin = true;
        }

        var input = ProvisionUserClaims.ReadCore(principal) with
        {
            Role = is_admin ? "Admin" : null,
        };

        return Task.FromResult(input);
    }

    public void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input)
    {
        if (input.Role != null)
            principal.Identities.First().AddClaim(new Claim("mercury.role", input.Role));
    }
}
