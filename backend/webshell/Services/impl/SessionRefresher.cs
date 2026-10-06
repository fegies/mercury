using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace backend.Services;

/// <summary>
/// Keeps authenticated sessions alive without re-prompting the user: when the
/// stored access token is about to expire and a refresh token is available,
/// it is exchanged at the token endpoint, the session's tokens are rotated,
/// and the IdP claims (role groups) are refreshed from the freshly issued ID
/// token before re-provisioning — so a role change at the provider takes
/// effect within one refresh cycle instead of at the next full login.
/// Rejects the principal when the IdP refuses the refresh (revoked/expired
/// session), and leaves everything untouched on transient failures so a
/// later request can retry.
/// </summary>
class SessionRefresher(
	IServiceScopeFactory scopeFactory,
	IHttpClientFactory httpClientFactory,
	IOptionsMonitor<OpenIdConnectOptions> oidcOptions,
	ILogger<SessionRefresher> logger)
{
	/// <summary>Item key under which the access token's expiry is stored in the auth ticket.</summary>
	public const string AccessExpiryItem = "mercury:access_expires_at";

	private const string RefreshTokenName = "refresh_token";
	private static readonly TimeSpan RefreshAhead = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan DefaultAccessTokenLifetime = TimeSpan.FromHours(1);

	/// <summary>
	/// Claim types never replaced during a claim refresh: technical token
	/// metadata the ticket does not need, plus the OIDC subject — the
	/// provisioning identity keying must stay exactly as the login flow
	/// established it.
	/// </summary>
	private static readonly HashSet<string> PreservedClaimTypes =
	[
		"sub", "iss", "aud", "exp", "iat", "nbf", "jti", "at_hash", "c_hash",
		"s_hash", "nonce", "azp", "amr", "auth_time", "sid", "kid", "alg", "typ",
		"local_userid", "mercury.role",
	];

	private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = [];

	public async Task RefreshIfDueAsync(CookieValidatePrincipalContext context)
	{
		var principal = context.Principal;
		if (principal is null)
			return;

		var refreshToken = context.Properties.GetTokenValue(RefreshTokenName);
		if (refreshToken is null)
			return;

		if (!context.Properties.Items.TryGetValue(AccessExpiryItem, out var rawExpiry)
			|| !DateTimeOffset.TryParse(rawExpiry, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var accessExpiry))
			return;

		if (accessExpiry - DateTimeOffset.UtcNow > RefreshAhead)
			return;

		if (!Guid.TryParse(principal.FindFirstValue("local_userid"), out var localUserId))
			return;

		// One refresh per user at a time; concurrent requests keep riding the
		// still-valid cookie and are renewed by the winner.
		var gate = _gates.GetOrAdd(localUserId, _ => new SemaphoreSlim(1, 1));
		if (!await gate.WaitAsync(TimeSpan.Zero))
			return;

		try
		{
			if (IsStillFresh(context.Properties))
				return;
			await RefreshAsync(context, principal, refreshToken);
		}
		finally
		{
			gate.Release();
		}
	}

	private static bool IsStillFresh(AuthenticationProperties properties)
		=> properties.Items.TryGetValue(AccessExpiryItem, out var raw)
			&& DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var expiry)
			&& expiry - DateTimeOffset.UtcNow > RefreshAhead;

	private async Task RefreshAsync(CookieValidatePrincipalContext context, ClaimsPrincipal principal, string refreshToken)
	{
		var oidc = oidcOptions.CurrentValue;
		var tokenEndpoint = oidc.Configuration?.TokenEndpoint;
		if (tokenEndpoint is null)
		{
			logger.SessionRefreshSkipped("OIDC metadata (token endpoint) is not available yet.");
			return;
		}

		try
		{
			var client = httpClientFactory.CreateClient();
			using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "refresh_token",
					["refresh_token"] = refreshToken,
					["client_id"] = oidc.ClientId ?? string.Empty,
					["client_secret"] = oidc.ClientSecret ?? string.Empty,
				}),
			};

			using var response = await client.SendAsync(request);
			var body = await response.Content.ReadAsStringAsync();

			if (!response.IsSuccessStatusCode)
			{
				if (IsPermanentRefreshFailure(body))
				{
					logger.SessionRefreshRejected(response.StatusCode);
					context.RejectPrincipal();
				}
				else
				{
					logger.SessionRefreshSkipped($"token endpoint returned {(int)response.StatusCode}.");
				}
				return;
			}

			using var json = JsonDocument.Parse(body);
			var root = json.RootElement;

			var accessToken = root.TryGetProperty("access_token", out var accessTokenNode) ? accessTokenNode.GetString() : null;
			if (accessToken is null)
			{
				logger.SessionRefreshSkipped("token endpoint response is missing access_token.");
				return;
			}

			var refreshedRefreshToken = root.TryGetProperty("refresh_token", out var refreshTokenNode) ? refreshTokenNode.GetString() : null;
			var idToken = root.TryGetProperty("id_token", out var idTokenNode) ? idTokenNode.GetString() : null;
			var lifetime = root.TryGetProperty("expires_in", out var expiresInNode) && expiresInNode.TryGetInt64(out var seconds)
				&& seconds > 0
					? TimeSpan.FromSeconds(seconds)
					: DefaultAccessTokenLifetime;

			context.Properties.UpdateTokenValue("access_token", accessToken);
			if (refreshedRefreshToken is not null)
				context.Properties.UpdateTokenValue(RefreshTokenName, refreshedRefreshToken);
			context.Properties.Items[AccessExpiryItem] =
				DateTimeOffset.UtcNow.Add(lifetime).ToString("o", CultureInfo.InvariantCulture);

			if (idToken is not null)
				await RefreshClaimsFromIdToken(principal, oidc, idToken);

			// Re-provision against the refreshed claims without touching the
			// stored profile picture (Entra would otherwise re-fetch Graph on
			// every refresh, and a null access token would clear it).
			try
			{
				using var scope = scopeFactory.CreateScope();
				await scope.ServiceProvider.GetRequiredService<UserProvisionService>()
					.ProvisionUser(principal, accessToken: null, preserveProfilePicture: true, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// Provisioning must never take the auth pipeline down; the next
				// refresh retries it.
				logger.SessionReprovisionFailed(ex);
			}

			context.ReplacePrincipal(principal);
			context.ShouldRenew = true;
			logger.SessionRefreshed();
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
		{
			logger.SessionRefreshSkipped($"token endpoint call failed: {ex.Message}");
		}
	}

	private static bool IsPermanentRefreshFailure(string body)
	{
		try
		{
			using var json = JsonDocument.Parse(body);
			return json.RootElement.TryGetProperty("error", out var error)
				&& error.GetString() is "invalid_grant" or "invalid_client" or "unauthorized_client";
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private async Task RefreshClaimsFromIdToken(ClaimsPrincipal principal, OpenIdConnectOptions oidc, string idToken)
	{
		var configuration = oidc.Configuration;
		if (configuration is null || oidc.ClientId is null)
			return;

		try
		{
			var validationParameters = new TokenValidationParameters
			{
				ValidIssuer = configuration.Issuer,
				ValidAudience = oidc.ClientId,
				IssuerSigningKeys = configuration.SigningKeys,
				RequireSignedTokens = true,
				RequireExpirationTime = true,
			};

			var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, validationParameters);
			if (!result.IsValid || result.ClaimsIdentity is not { } identity)
			{
				logger.IdTokenValidationFailed();
				return;
			}

			ReplaceRefreshedClaims(principal, identity.Claims);
		}
		catch (Exception ex)
		{
			logger.IdTokenValidationFailed(ex);
		}
	}

	private static void ReplaceRefreshedClaims(ClaimsPrincipal principal, IEnumerable<Claim> fresh)
	{
		foreach (var group in fresh.Where(c => !PreservedClaimTypes.Contains(c.Type)).GroupBy(c => c.Type))
		{
			foreach (var identity in principal.Identities)
			{
				foreach (var stale in identity.FindAll(group.Key).ToList())
					identity.TryRemoveClaim(stale);
				foreach (var claim in group)
					identity.AddClaim(new Claim(claim.Type, claim.Value, claim.ValueType, claim.Issuer));
			}
		}
	}
}

internal static class SessionRefresherLogs
{
	public static void SessionRefreshed(this ILogger<SessionRefresher> logger)
		=> logger.LogInformation("OIDC session refreshed.");

	public static void SessionRefreshRejected(this ILogger<SessionRefresher> logger, HttpStatusCode status)
		=> logger.LogWarning("OIDC session refresh rejected by the token endpoint ({Status}); rejecting the session.", status);

	public static void SessionRefreshSkipped(this ILogger<SessionRefresher> logger, string reason)
		=> logger.LogWarning("OIDC session refresh skipped: {Reason}", reason);

	public static void SessionReprovisionFailed(this ILogger<SessionRefresher> logger, Exception ex)
		=> logger.LogWarning(ex, "Re-provisioning after the session refresh failed; will retry on a later request.");

	public static void IdTokenValidationFailed(this ILogger<SessionRefresher> logger, Exception? ex = null)
		=> logger.LogWarning(ex, "Validating the refreshed ID token failed; claims were not refreshed.");
}
