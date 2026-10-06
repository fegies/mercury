using System.Net;
using System.Security.Claims;
using System.Text;
using appcore.Configuration;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Infra.Events;
using appcore.Services;
using appcore.Tests.Infrastructure;
using backend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace appcore.Tests.Services;

public class SessionRefresherTests
{
	private const string Issuer = "https://idp.example.org";
	private const string TokenEndpoint = "https://idp.example.org/oauth/token";
	private const string ClientId = "mercury";
	private static readonly byte[] SigningKeyBytes = "test-signing-key-0123456789abcdef"u8.ToArray();

	[Fact]
	public async Task WithoutRefreshToken_DoesNothing()
	{
		var (refresher, endpoint, context) = await CreateSession();
		context.Properties.StoreTokens(
			context.Properties.GetTokens().Where(t => t.Name != "refresh_token").ToArray());

		await refresher.RefreshIfDueAsync(context);

		Assert.Equal(0, endpoint.RequestCount);
		Assert.False(context.ShouldRenew);
		Assert.NotEmpty(context.Principal!.Identities);
	}

	[Fact]
	public async Task WhenNotDue_DoesNothing()
	{
		var (refresher, endpoint, context) = await CreateSession();
		context.Properties.Items[SessionRefresher.AccessExpiryItem] =
			DateTimeOffset.UtcNow.AddHours(2).ToString("o");

		await refresher.RefreshIfDueAsync(context);

		Assert.Equal(0, endpoint.RequestCount);
		Assert.False(context.ShouldRenew);
	}

	[Fact]
	public async Task DueRefresh_RotatesTokensReplacesClaimsAndReprovisions()
	{
		var (refresher, endpoint, context) = await CreateSession();
		endpoint.Response = (HttpStatusCode.OK,
			"""{"access_token":"access-new","refresh_token":"refresh-2","expires_in":3600,"id_token":"<id-token>"}""");

		await refresher.RefreshIfDueAsync(context);

		Assert.Equal(1, endpoint.RequestCount);
		Assert.Equal("access-new", context.Properties.GetTokenValue("access_token"));
		Assert.Equal("refresh-2", context.Properties.GetTokenValue("refresh_token"));
		Assert.True(context.ShouldRenew);
		Assert.NotEmpty(context.Principal!.Identities);

		// The ID token carried a fresh name; the principal's claim was replaced.
		Assert.Equal("Jane Smith", context.Principal.FindFirstValue("name"));
	}

	[Fact]
	public async Task DueRefresh_RoleDemotion_TakesEffectImmediately()
	{
		var (refresher, endpoint, context) = await CreateSession();
		endpoint.Response = (HttpStatusCode.OK,
			"""{"access_token":"access-new","refresh_token":"refresh-2","expires_in":3600,"id_token":"<id-token>"}""");

		await refresher.RefreshIfDueAsync(context);

		// The ID token dropped role.admin; the stale mercury.role stamp is gone
		// and a UserRoleChanged event was appended.
		Assert.Null(context.Principal!.FindFirstValue("mercury.role"));
		var store = endpoint.Services.GetRequiredService<InMemoryEventStore>();
		Assert.Contains(store.Events, e => e.EventType == EventTypeNames.UserRoleChanged);
	}

	[Fact]
	public async Task PermanentRefreshFailure_RejectsThePrincipal()
	{
		var (refresher, endpoint, context) = await CreateSession();
		endpoint.Response = (HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

		await refresher.RefreshIfDueAsync(context);

		Assert.Equal(1, endpoint.RequestCount);
		Assert.False(context.ShouldRenew);
		Assert.True(context.Principal?.Identities.Any() != true);
	}

	[Fact]
	public async Task TransientRefreshFailure_KeepsTheSession()
	{
		var (refresher, endpoint, context) = await CreateSession();
		endpoint.Response = (HttpStatusCode.ServiceUnavailable, """{"error":"temporarily_unavailable"}""");

		await refresher.RefreshIfDueAsync(context);

		Assert.Equal(1, endpoint.RequestCount);
		Assert.False(context.ShouldRenew);
		Assert.NotEmpty(context.Principal!.Identities);
		Assert.Equal("access-old", context.Properties.GetTokenValue("access_token"));
	}

	[Fact]
	public async Task ConcurrentRefresh_IsSerialisedPerUser()
	{
		var (refresher, endpoint, context) = await CreateSession();
		endpoint.Response = (HttpStatusCode.OK,
			"""{"access_token":"access-new","refresh_token":"refresh-2","expires_in":3600}""");
		endpoint.HoldRequests = new TaskCompletionSource();

		var first = refresher.RefreshIfDueAsync(context);
		var deadline = DateTime.UtcNow.AddSeconds(2);
		while (endpoint.RequestCount == 0 && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		var second = refresher.RefreshIfDueAsync(context);
		await second;

		Assert.Equal(1, endpoint.RequestCount);
		endpoint.HoldRequests.TrySetResult();
		await first;
	}

	// ---- harness ------------------------------------------------------------

	private static async Task<(SessionRefresher, CountingTokenEndpoint, CookieValidatePrincipalContext)> CreateSession()
	{
		var services = new ServiceCollection();
		var store = new InMemoryEventStore();
		services.AddSingleton(store);
		services.AddSingleton<IEventStore>(store);
		services.AddSingleton<IEventReader>(store);
		services.AddSingleton<UserService>();
		services.AddSingleton(new EventHandlerOptions());
		services.AddSingleton<IUserProvisioner, GenericUserProvisioner>();
		services.AddSingleton<IDecisionFunction<ProvisionUserInput, UserProvisionResult>, ProvisionUserEvaluator>();
		services.AddSingleton<IncomingEventHandler<ProvisionUserInput, UserProvisionResult>>();
		services.AddSingleton<UserProvisionService>();
		var provider = services.BuildServiceProvider();

		var endpoint = new CountingTokenEndpoint(provider);

		var configuration = new OpenIdConnectConfiguration
		{
			Issuer = Issuer,
			TokenEndpoint = TokenEndpoint,
		};
		configuration.SigningKeys.Add(new SymmetricSecurityKey(SigningKeyBytes));
		var oidcOptions = new OpenIdConnectOptions
		{
			ClientId = ClientId,
			ClientSecret = "secret",
			Configuration = configuration,
		};

		var principal = new ClaimsPrincipal(new ClaimsIdentity(
		[
			new("sub", "sub-1"),
			new("name", "Jane Doe"),
			new("email", "jane@example.org"),
			new("roles", """["role.admin"]"""),
			new("mercury.role", "Admin"),
		], "auth"));

		// Provision the user up front so the refresh path re-provisions an
		// existing identity (role change → UserRoleChanged) instead of forking
		// a second user.
		await provider.GetRequiredService<UserProvisionService>()
			.ProvisionUser(principal, accessToken: null, CancellationToken.None);

		var refresher = new SessionRefresher(
			provider.GetRequiredService<IServiceScopeFactory>(),
			new StubHttpClientFactory(endpoint),
			new StaticOptionsMonitor(oidcOptions),
			NullLogger<SessionRefresher>.Instance);

		var properties = new AuthenticationProperties();
		properties.StoreTokens(
		[
			new AuthenticationToken { Name = "access_token", Value = "access-old" },
			new AuthenticationToken { Name = "refresh_token", Value = "refresh-1" },
		]);
		properties.Items[SessionRefresher.AccessExpiryItem] =
			DateTimeOffset.UtcNow.AddSeconds(60).ToString("o");

		var httpContext = new DefaultHttpContext { RequestServices = provider };
		var scheme = new AuthenticationScheme(
			CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
		var ticket = new AuthenticationTicket(principal, properties, scheme.Name);
		var context = new CookieValidatePrincipalContext(httpContext, scheme, new CookieAuthenticationOptions(), ticket);

		return (refresher, endpoint, context);
	}

	private static string CreateIdToken(Dictionary<string, object> claims)
	{
		var descriptor = new SecurityTokenDescriptor
		{
			Issuer = Issuer,
			Audience = ClientId,
			SigningCredentials = new SigningCredentials(
				new SymmetricSecurityKey(SigningKeyBytes), SecurityAlgorithms.HmacSha256),
			Claims = claims,
			Expires = DateTime.UtcNow.AddMinutes(5),
			IssuedAt = DateTime.UtcNow,
		};
		return new JsonWebTokenHandler().CreateToken(descriptor);
	}

	private sealed class StubHttpClientFactory(CountingTokenEndpoint endpoint) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(endpoint);
	}

	private sealed class StaticOptionsMonitor(OpenIdConnectOptions options) : IOptionsMonitor<OpenIdConnectOptions>
	{
		public OpenIdConnectOptions CurrentValue => options;
		public OpenIdConnectOptions Get(string? name) => options;
		public IDisposable? OnChange(Action<OpenIdConnectOptions, string?> listener) => null;
	}

	private sealed class CountingTokenEndpoint(IServiceProvider services) : HttpMessageHandler
	{
		public (HttpStatusCode, string) Response = (HttpStatusCode.OK, "{}");
		public int RequestCount { get; private set; }
		public TaskCompletionSource? HoldRequests { get; set; }
		public IServiceProvider Services => services;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			RequestCount++;

			if (HoldRequests is { } hold)
			{
				await hold.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
				return new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("{}", Encoding.UTF8, "application/json"),
				};
			}

			var (status, body) = Response;

			if (body.Contains("<id-token>"))
			{
				var roles = body.Contains("role.admin")
					? """["role.admin"]"""
					: """["role.member"]""";
				var idToken = CreateIdToken(new Dictionary<string, object>
				{
					["iss"] = Issuer,
					["aud"] = ClientId,
					["sub"] = "sub-1",
					["name"] = "Jane Smith",
					["email"] = "jane@example.org",
					["roles"] = roles,
				});
				body = body.Replace("<id-token>", idToken);
			}

			return new HttpResponseMessage(status)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json"),
			};
		}
	}
}
