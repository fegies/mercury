using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using appcore.Infra.Evaluators;

namespace backend.Services;

class ZitadelUserProvisioner : IUserProvisioner
{
    public ProvisionUserInput BuildInput(ClaimsPrincipal principal)
    {
        var picture = principal.FindFirstValue("picture");

        var is_admin = false;
        var roles_claim = principal.FindFirstValue("urn:zitadel:iam:org:project:roles");
        if (roles_claim != null)
        {
            var roles_dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(roles_claim);
            if (roles_dict?.ContainsKey("role.admin") == true)
                is_admin = true;
        }

        return ProvisionUserClaims.ReadCore(principal) with
        {
            ProfilePictureUrl = picture,
            Role = is_admin ? "Admin" : null,
        };
    }

    public void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input)
    {
        if (input.Role != null)
            principal.Identities.First().AddClaim(new Claim("mercury.role", input.Role));
    }
}
