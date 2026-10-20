using System.Net.Http.Headers;
using System.Security.Claims;
using appcore.Infra.Evaluators;
using backend.Configuration;

namespace backend.Services;

class EntraUserProvisioner(EntraConfigurationValue entraConfig, IImageStorage imageStorage, IHttpClientFactory httpClientFactory) : IUserProvisioner
{
    private const string GraphPhotoUrl = "https://graph.microsoft.com/v1.0/me/photo/$value";
    private const string ProfilePictureArea = "users";

    public async Task<ProvisionUserInput> BuildInputAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        var core = ProvisionUserClaims.ReadCore(principal);

        var roles = ReadRoleClaims(principal);
        var is_admin = roles.Any(r => entraConfig.AdminAppRoles.Contains(r));

        string? pictureUrl = null;
        if (accessToken != null)
        {
            var storedId = await StoreProfilePictureAsync(accessToken, ct);
            if (storedId.HasValue)
                pictureUrl = $"/api/profilepictures/{storedId}";
        }

        return core with
        {
            ProfilePictureUrl = pictureUrl,
            Role = is_admin ? "Admin" : null,
        };
    }

    public void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input)
    {
        if (input.Role != null)
            principal.Identities.First().AddClaim(new Claim("mercury.role", input.Role));
    }

    public Task RollbackPendingWritesAsync(CancellationToken ct)
        => pendingPictureId is { } id ? imageStorage.DeleteAsync(ProfilePictureArea, id, ct) : Task.CompletedTask;

    private Guid? pendingPictureId;

    // Entra emits app role assignments in the 'roles' claim. The JWT handler
    // normally preserves array claims as multiple same-type claims, but the
    // OIDC handler can flatten them into a space-delimited string, so accept
    // both forms.
    private static IEnumerable<string> ReadRoleClaims(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirst("roles");
        if (claim is null)
            return [];
        if (claim.Value.Contains(' '))
            return claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return principal.FindAll("roles").Select(c => c.Value);
    }

    private async Task<Guid?> StoreProfilePictureAsync(string accessToken, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, GraphPhotoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var storedId = await imageStorage.StoreAsync(ProfilePictureArea, stream, ct);
        pendingPictureId = storedId;
        return storedId;
    }
}
