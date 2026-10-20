using System.Security.Claims;
using System.Text.Json;
using appcore.Infra.Evaluators;

namespace backend.Services;

class GenericUserProvisioner : IUserProvisioner
{
    public Task<ProvisionUserInput> BuildInputAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        // profile pictures are not standardised. Because of that, we cannot handle it without knowing
        // more details on the specific provider type.
        var is_admin = ReadRoleClaims(principal).Contains("role.admin");

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

    // Providers emit the role list in the 'roles' claim in different shapes:
    // multiple same-type claims (the usual case with MapInboundClaims=false),
    // a single claim holding a JSON array, or a single claim holding
    // space-delimited values (the OIDC handler flattening). Accept all three.
    private static IReadOnlyList<string> ReadRoleClaims(ClaimsPrincipal principal)
    {
        var values = principal.FindAll("roles").Select(c => c.Value).ToList();
        if (values.Count == 1)
        {
            var single = values[0];
            if (single.StartsWith('['))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<string[]>(single);
                    if (parsed != null)
                        return parsed;
                }
                catch (JsonException)
                {
                    // fall through to the space-delimited form
                }
            }
            if (single.Contains(' '))
                return single.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        return values;
    }
}
