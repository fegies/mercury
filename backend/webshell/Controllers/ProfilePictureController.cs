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

        return PhysicalFile(path, "application/octet-stream");
    }
}
