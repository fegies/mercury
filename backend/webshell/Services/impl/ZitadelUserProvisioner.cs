using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using appcore.Data;
using appcore.Entities;

namespace backend.Services;

class ZitadelUserProvisioner(ApplicationDbContext context) : IUserProvisioner
{
    public async Task ProvisionUser(UserEntity user, ClaimsPrincipal principal)
    {
        var picture = principal.FindFirstValue("picture");

        user.ProfilePictureUrl = picture;

        var roles_claim = principal.FindFirstValue("urn:zitadel:iam:org:project:roles");
        if (roles_claim != null)
        {
            var roles_dict = JsonSerializer.Deserialize<Dictionary<string, JsonValue>>(roles_claim);
            if (roles_dict?.ContainsKey("role.admin") == true)
            {
                principal.Identities.First().AddClaim(new Claim("mercury.role", "Admin"));
            }
        }

        await context.SaveChangesAsync();
    }
}
