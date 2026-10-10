using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

/// <summary>
/// Serves user profile pictures stored locally.
/// </summary>
[Route("api/profilepictures")]
[ApiController]
public class ProfilePictureController(IImageStorage imageStorage) : ControllerBase
{
    private const string ProfilePictureArea = "users";

    /// <summary>
    /// Serves a profile picture by id.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult GetProfilePicture(Guid id)
    {
        var path = imageStorage.ResolvePath(ProfilePictureArea, id);
        if (path is null)
            return NotFound();

        // Picture blobs are immutable per id (updates are stored under a
        // fresh id and a fresh URL); avatars render on every page, so
        // letting the browser keep them for a day removes most of that
        // load. Private keeps shared caches out of the authenticated
        // content path.
        Response.Headers.CacheControl = "private, max-age=86400";
        return PhysicalFile(path, "application/octet-stream");
    }
}
