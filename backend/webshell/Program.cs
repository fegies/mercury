using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Text;
using backend.Auth;
using backend.Configuration;
using backend.Errors;
using backend.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using System.Net;

using appcore;
using appcore.Infra;
using appcore.Infra.Evaluators;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var IsRealLaunch = Assembly.GetEntryAssembly() == Assembly.GetExecutingAssembly();

var config = new BackendConfig();
builder.Configuration.Bind(config);
if (IsRealLaunch)
{
    config.Validate();

    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")))
        throw new InvalidOperationException(
            "No PostgreSQL connection string configured. Set ConnectionStrings__DefaultConnection "
            + "(Npgsql format, e.g. \"Host=db;Port=5432;Database=mercury;Username=mercury;Password=...\").");
}

builder.RegisterAppcoreServices();

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.AuctionConfig);
builder.Services.AddScoped<UserProvisionService>();
builder.Services.AddSingleton<IImageStorage>(new ImageStorageService(config.ImageStorage.BasePath!));

switch (config.OidcConfig.ProviderType)
{
    case OidcConfigurationValue.ProviderTypeValue.Zitadel:
        builder.Services.AddScoped<IUserProvisioner, ZitadelUserProvisioner>();
        break;
    case OidcConfigurationValue.ProviderTypeValue.Entra:
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<IUserProvisioner>(sp => new EntraUserProvisioner(
            config.EntraConfig,
            sp.GetRequiredService<IImageStorage>(),
            sp.GetRequiredService<IHttpClientFactory>()));
        break;
    default:
        builder.Services.AddScoped<IUserProvisioner, GenericUserProvisioner>();
        break;
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie()
.AddOpenIdConnect(options =>
{
    var oidcConf = config.OidcConfig;
    options.Authority = oidcConf.AuthorityUrl;
    // Metadata over plain HTTP is only acceptable for non-production
    // authorities (e.g. a local mock IdP); HTTPS is still enforced for
    // HTTPS authorities.
    options.RequireHttpsMetadata = !(oidcConf.AuthorityUrl?.StartsWith("http://") ?? false);
    options.ClientId = oidcConf.ClientId;
    options.ClientSecret = oidcConf.ClientSecret;
    options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.ResponseType = OpenIdConnectResponseType.Code;

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");

    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;
    options.TokenValidationParameters.RoleClaimType = "role";
    options.MapInboundClaims = false;

    options.Events.OnTokenValidated = async (ctx) =>
    {
        var accessToken = ctx.ProtocolMessage?.AccessToken;
        await ctx.HttpContext.RequestServices.GetRequiredService<UserProvisionService>().ProvisionUser(ctx.Principal!, accessToken, CancellationToken.None);
    };
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict;
});

builder.Services.AddControllers();

// Reconstruct the original request's scheme, host, and client IP from the
// standard X-Forwarded-* headers set by the reverse proxy chain in front of
// this backend (e.g. the container's nginx, plus the TLS terminator ahead of
// it). Loopback proxies (the container's nginx) are trusted by default;
// additional ones are configured via ForwardedConfig__TrustedProxies__N.
// The flags-enum cannot be bound from configuration (the binder does not
// convert the string form), so the defaults live here in code.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;

    foreach (var proxy in config.ForwardedConfig.TrustedProxies)
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi("backend", options =>
{
    options.ShouldInclude = (_) => true;
    options.AddOperationTransformer((operation, context, _) =>
    {
        // Parameters annotated [FromCurrentUser] are bound from the authenticated user's
        // claims, not the client, so they must not appear in the generated API document.
        var claimBoundParameters = context.Description.ParameterDescriptions
            .Where(p => p.ParameterDescriptor is ControllerParameterDescriptor cpd
                        && cpd.ParameterInfo.IsDefined(typeof(FromCurrentUserAttribute), inherit: false))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        operation.Parameters = operation.Parameters?
            .Where(p => p.Name is null || !claimBoundParameters.Contains(p.Name))
            .ToList();
        return Task.CompletedTask;
    });
});

builder.Services.AddSingleton<IAuthorizationHandler, IsAdminRequirementHandler>();

var requireAuthPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("IsAdmin", p => p.AddRequirements(new IsAdminRequirement()))
    .SetFallbackPolicy(requireAuthPolicy);

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (WebStatusException e)
    {
        ctx.Response.StatusCode = e.StatusCode;
        if (e.Message != null)
        {
            Encoding.UTF8.GetBytes(e.Message, ctx.Response.BodyWriter);
            await ctx.Response.BodyWriter.FlushAsync();
        }
    }
    catch (InvariantViolation e)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        Encoding.UTF8.GetBytes(e.Message, ctx.Response.BodyWriter);
        await ctx.Response.BodyWriter.FlushAsync();
    }
});

app.UseRouting();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


// app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();



app.MapControllers();

if (IsRealLaunch)
{
    // this at least looks like a real startup. 
    await EventStoreSchema.EnsureCreatedAsync(app.Services.GetRequiredService<NpgsqlDataSource>());
}

app.Run();
