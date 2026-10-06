using System;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace backend.Controllers
{
    /// <summary>
    /// Methods on how to interact with the sign-in / out part
    /// </summary>
    [ApiController]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class OidcController : ControllerBase
    {
        /// <summary>
        /// Sign out the current user by clearing the session cookie. With
        /// <c>idp=1</c> the identity provider's SSO session is ended too
        /// (RP-initiated logout): the IdP must have the absolute
        /// <c>/signedout</c> URL allowlisted as a post-logout redirect URI.
        /// </summary>
        [HttpPost("api/logout")]
        public ActionResult Logout([FromQuery] bool idp = false)
        {
            // State-changing and cookie-carrying: refuse cross-site origins.
            // SameSite=Lax already keeps the cookie off cross-site POSTs;
            // this is the belt to those braces.
            if (!HasTrustedOrigin())
                return BadRequest("logout must be requested from this site.");

            return idp
                ? SignOut(SignOutProperties(), OpenIdConnectDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme)
                : SignOut(SignOutProperties(), CookieAuthenticationDefaults.AuthenticationScheme);
        }

        private AuthenticationProperties SignOutProperties()
            => new()
            {
                RedirectUri = $"{Request.Scheme}://{Request.Host.Value}/signedout",
            };

        private bool HasTrustedOrigin()
        {
            var origin = Request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin))
                return true;
            return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
                && string.Equals(parsed.Scheme, Request.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(parsed.Authority, Request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A static page shown after signout
        /// </summary>
        [HttpGet("/signedout")]
        [AllowAnonymous]
        public string SignedOutPage()
        {
            return "You are logged out";
        }

        /// <summary>
        /// Begin the oidc sign-in flow.
        /// </summary>
        /// <param name="return_to">An optional local page to redirect to after signin</param>
        [HttpGet("api/login")]
        [EnableRateLimiting("login")]
        public ActionResult Login([FromQuery] string? return_to)
        {
            if (return_to != null)
            {
                if (!Url.IsLocalUrl(return_to))
                    return BadRequest("return_to must be a local URL.");
                return LocalRedirect(return_to);
            }

            var claims = User.Claims.GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToList());

            return Ok(claims);
        }
    }
}
