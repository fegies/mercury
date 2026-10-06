using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

/// <summary>
/// Liveness probe endpoint for load balancers and container healthchecks.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class HealthController : ControllerBase
{
    /// <summary>
    /// Anonymous liveness probe; used by container healthchecks to traverse
    /// the full reverse-proxy chain.
    /// </summary>
    [HttpGet("/api/healthz")]
    [AllowAnonymous]
    public string Healthz() => "ok";
}
