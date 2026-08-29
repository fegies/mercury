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
    public async Task<ActionResult> GetProfilePicture(Guid id)
    {
        var bytes = await imageStorage.ReadAsync(ProfilePictureArea, id, CancellationToken.None);
        if (bytes is null)
            return NotFound();

        return File(bytes, "application/octet-stream");
    }
}
