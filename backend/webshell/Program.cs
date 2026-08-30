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
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using appcore;
using appcore.Infra;
using appcore.Infra.Evaluators;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var IsRealLaunch = Assembly.GetEntryAssembly() == Assembly.GetExecutingAssembly();

var config = new BackendConfig();
builder.Configuration.Bind(config);
if (IsRealLaunch)
    config.Validate();

builder.RegisterAppcoreServices();

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.AuctionConfig);
builder.Services.AddScoped<UserProvisionService>();
builder.Services.AddSingleton<IImageStorage, ImageStorageService>();

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
        builder.Services.AddScoped<IUserProvisioner, GeneriUserProvisioner>();
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
            .Where(p => !claimBoundParameters.Contains(p.Name))
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
