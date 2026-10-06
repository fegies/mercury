using System.Text.Json;
using appcore.Infra.Live;
using backend.Auth;
using backend.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[Route("api/events")]
[ApiController]
public class LiveController(LiveEventHub hub) : ControllerBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Streams live events to the signed-in user as server-sent events:
    /// targeted notifications (outbid, won, cancelled) and broadcast pings
    /// telling clients that an auction changed and should be refetched.
    /// Transient by design — only events committed while the stream is open
    /// are delivered; clients reconnect automatically.
    /// </summary>
    [HttpGet("stream")]
    [Authorize]
    [ProducesResponseType(typeof(LiveEvent), StatusCodes.Status200OK, "text/event-stream")]
    public async Task GetStream([FromCurrentUser] Guid currentUserId, CancellationToken ct)
    {
        if (!hub.TryConnect(currentUserId, out var connection))
            throw new TooManyRequestsException(
                "Too many live connections for this user; close unused tabs and reconnect.");
        try
        {
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            // Deactivates nginx response buffering for this stream in production.
            Response.Headers["X-Accel-Buffering"] = "no";

            var reader = connection.Reader;
            using var keepAlive = new PeriodicTimer(TimeSpan.FromSeconds(25));
            // One outstanding waiter per source: PeriodicTimer throws if a
            // second WaitForNextTickAsync is issued while one is still pending,
            // so both tasks are reused across iterations.
            var read = reader.WaitToReadAsync(ct).AsTask();
            var ping = keepAlive.WaitForNextTickAsync(ct).AsTask();
            while (true)
            {
                var completed = await Task.WhenAny(read, ping);

                if (completed == ping)
                {
                    if (!await ping)
                        break;
                    await Response.WriteAsync(": keep-alive\n\n", ct);
                    ping = keepAlive.WaitForNextTickAsync(ct).AsTask();
                }
                else
                {
                    if (!await read)
                        break;
                    while (reader.TryRead(out var live))
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(live, SerializerOptions)}\n\n", ct);
                    read = reader.WaitToReadAsync(ct).AsTask();
                }
            }
        }
        finally
        {
            connection.Dispose();
        }
    }
}
