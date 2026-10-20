using System.Net.Http.Headers;
using System.Security.Claims;
using appcore.Infra.Evaluators;

namespace backend.Services;

class EntraUserProvisioner(GenericUserProvisioner generic, IImageStorage imageStorage, IHttpClientFactory httpClientFactory) : IUserProvisioner
{
    private const string GraphPhotoUrl = "https://graph.microsoft.com/v1.0/me/photo/$value";
    private const string ProfilePictureArea = "users";

    public async Task<ProvisionUserInput> BuildInputAsync(ClaimsPrincipal principal, string? accessToken, CancellationToken ct)
    {
        // Role mapping matches the generic provider: Admin is granted when the
        // 'roles' claim contains the 'role.admin' app role value.
        var input = await generic.BuildInputAsync(principal, accessToken, ct);

        string? pictureUrl = null;
        if (accessToken != null)
        {
            var storedId = await StoreProfilePictureAsync(accessToken, ct);
            if (storedId.HasValue)
                pictureUrl = $"/api/profilepictures/{storedId}";
        }

        return input with { ProfilePictureUrl = pictureUrl };
    }

    public void ApplyClaims(ClaimsPrincipal principal, ProvisionUserInput input)
        => generic.ApplyClaims(principal, input);

    public Task RollbackPendingWritesAsync(CancellationToken ct)
        => pendingPictureId is { } id ? imageStorage.DeleteAsync(ProfilePictureArea, id, ct) : Task.CompletedTask;

    private Guid? pendingPictureId;

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
