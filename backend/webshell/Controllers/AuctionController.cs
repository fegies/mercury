using System.Security.Cryptography;
using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using backend.Errors;
using backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[Route("api/auctions")]
[ApiController]
public class AuctionController(
    AuctionService auctionService,
    IncomingEventHandler<AuctionCreated, Guid> createAuctionHandler,
    IncomingEventHandler<AuctionUpdated, bool> updateAuctionHandler,
    IncomingEventHandler<AuctionImagesRemoved, bool> removeImagesHandler,
    IncomingEventHandler<AuctionClosed, bool> closeAuctionHandler,
    IncomingEventHandler<AuctionImagesAdded, bool> addImagesHandler,
    IImageStorage imageStorage) : ControllerBase
{
    private const string ImageArea = "auctions";

    public record CreateAuctionRequest
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required decimal MinimumPrice { get; init; }
        public required DateTime ClosureTime { get; init; }
        public required bool IsPublished { get; init; }
    }

    public record UpdateAuctionRequest
    {
        public string? Title { get; init; }
        public string? Description { get; init; }
        public decimal? MinimumPrice { get; init; }
        public DateTime? ClosureTime { get; init; }
        public bool? IsPublished { get; init; }
    }

    public record UploadImagesRequest
    {
        public List<IFormFile> Files { get; init; } = [];
    }

    public record ImageDto
    {
        public Guid Id { get; init; }
        public string Url { get; init; } = "";
    }

    /// <summary>
    /// Creates a new auction and returns its id.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<Guid> CreateAuction([FromBody] CreateAuctionRequest request, CancellationToken ct)
    {
        var auctionId = Guid.NewGuid();

        var createdEvent = new AuctionCreated
        {
            AuctionId = auctionId,
            Title = request.Title,
            Description = request.Description,
            MinimumPrice = request.MinimumPrice,
            ClosureTime = request.ClosureTime,
            IsPublished = request.IsPublished,
        };

        await createAuctionHandler.Execute(createdEvent, ct);

        return auctionId;
    }

    /// <summary>
    /// Updates the mutable fields of an auction. Closed auctions cannot be updated.
    /// </summary>
    [HttpPatch("{id}")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UpdateAuction(Guid id, [FromBody] UpdateAuctionRequest request, CancellationToken ct)
    {
        var updatedEvent = new AuctionUpdated
        {
            AuctionId = id,
            Title = request.Title,
            Description = request.Description,
            MinimumPrice = request.MinimumPrice,
            ClosureTime = request.ClosureTime,
            IsPublished = request.IsPublished,
        };

        await updateAuctionHandler.Execute(updatedEvent, ct);

        return NoContent();
    }

    /// <summary>
    /// Adds new images to an existing auction.
    /// </summary>
    [HttpPost("{id}/images")]
    [Consumes("multipart/form-data")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> UploadImages(Guid id, [FromForm] UploadImagesRequest request, CancellationToken ct)
    {
        if (request.Files.Count == 0)
            return BadRequest("No files uploaded.");

        await StoreAndAddImages(id, request.Files, ct);

        return NoContent();
    }

    /// <summary>
    /// Removes an image from an auction.
    /// </summary>
    [HttpDelete("{id}/images/{imageId}")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RemoveImage(Guid id, Guid imageId, CancellationToken ct)
    {
        var removedEvent = new AuctionImagesRemoved
        {
            AuctionId = id,
            ImageIds = [imageId],
        };

        await removeImagesHandler.Execute(removedEvent, ct);

        await imageStorage.DeleteAsync(ImageArea, imageId, CancellationToken.None);

        return NoContent();
    }

    /// <summary>
    /// Closes an auction permanently.
    /// </summary>
    [HttpPost("{id}/close")]
    [Authorize(Policy = "IsAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CloseAuction(Guid id, CancellationToken ct)
    {
        var closedEvent = new AuctionClosed
        {
            AuctionId = id,
            Reason = AuctionCloseReason.Manual,
        };

        await closeAuctionHandler.Execute(closedEvent, ct);

        return NoContent();
    }

    /// <summary>
    /// Lists all auctions.
    /// </summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType<List<AuctionSummary>>(StatusCodes.Status200OK)]
    public async Task<List<AuctionSummary>> ListAuctions(CancellationToken ct)
    {
        var auctions = await auctionService.ListAuctionSummaries();
        if (!IsAdmin)
            return auctions.Where(a => a.IsPublished).ToList();
        return auctions;
    }

    /// <summary>
    /// Returns a single auction by id.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize]
    [ProducesResponseType<AuctionSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<AuctionSummary> GetAuction(Guid id, CancellationToken ct)
    {
        var summary = await auctionService.GetAuctionSummary(id);
        if (summary is null)
            throw new NotFoundException();

        if (!IsAdmin && !summary.IsPublished)
            throw new NotFoundException();

        return summary;
    }

    /// <summary>
    /// Serves an auction image by id.
    /// </summary>
    [HttpGet("{auctionId}/images/{imageId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> ServeImage(Guid auctionId, Guid imageId)
    {
        var bytes = await imageStorage.ReadAsync(ImageArea, imageId, CancellationToken.None);
        if (bytes is null)
            return NotFound();

        return File(bytes, "application/octet-stream");
    }

    private bool IsAdmin => User.FindAll("mercury.role").Any(c => c.Value == "Admin");

    private async Task StoreAndAddImages(Guid auctionId, List<IFormFile> files, CancellationToken ct)
    {
        var imageRefs = new List<AuctionImageRef>();

        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, ct);
            var hash = Convert.ToHexString(hashBytes);

            stream.Position = 0;
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, ct);

            var imageId = await imageStorage.StoreAsync(ImageArea, memoryStream.ToArray(), ct);

            imageRefs.Add(new AuctionImageRef
            {
                Id = imageId,
                FilePath = imageId.ToString(),
                Hash = hash,
            });
        }

        var addedEvent = new AuctionImagesAdded
        {
            AuctionId = auctionId,
            Images = imageRefs,
        };

        await addImagesHandler.Execute(addedEvent, ct);
    }
}
