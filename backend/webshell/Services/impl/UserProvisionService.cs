using System.Security.Claims;
using appcore.Data;
using Microsoft.EntityFrameworkCore;

namespace backend.Services
{
    internal class UserProvisionService(ApplicationDbContext context, IUserProvisioner pictureProvisioner)
    {
        public async Task ProvisionUser(ClaimsPrincipal principal)
        {
            var sub = principal.FindFirst("sub") ?? throw new Exception("sub not found");
            var name = principal.FindFirst("name") ?? throw new Exception("name not found");
            var email = principal.FindFirst("email") ?? throw new Exception("email not found");

            var user = await context.Users.AsQueryable()
                .Where(u => u.OidIss == sub.Issuer && u.OidSub == sub.Value)
                .FirstOrDefaultAsync();

            if (user == null)
            {
                user = new()
                {
                    OidIss = sub.Issuer,
                    OidSub = sub.Value,
                    Name = name.Value,
                };
                context.Users.Add(user);
            }

            user.Email = email.Value;
            user.Name = name.Value;

            await context.SaveChangesAsync();

            // ensure that the local userid claim is not present
            var previous_id_claims = principal.FindAll("local_userid").ToList();
            if (previous_id_claims.Count > 0)
                foreach (var identity in principal.Identities)
                    foreach (var claim in previous_id_claims)
                        identity.TryRemoveClaim(claim);

            principal.Identities.First().AddClaim(new Claim("local_userid", user.Id.ToString()));

            await pictureProvisioner.ProvisionUser(user, principal);
        }
    }
}