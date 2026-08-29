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

        var groups = principal.FindAll("groups").Select(c => c.Value).ToList();
        var is_admin = groups.Any(g => entraConfig.AdminGroupIds.Contains(g));

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

    private async Task<Guid?> StoreProfilePictureAsync(string accessToken, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, GraphPhotoUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await imageStorage.StoreAsync(ProfilePictureArea, stream, ct);
    }
}
