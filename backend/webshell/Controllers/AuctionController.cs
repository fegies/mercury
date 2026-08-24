using System.Globalization;
using System.Security.Cryptography;
using appcore.Data;
using appcore.Entities;
using appcore.Entities.Events;
using appcore.Infra;
using appcore.Infra.Evaluators;
using appcore.Services;
using backend.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[Route("api/auctions")]
[ApiController]
public class AuctionController(AuctionService auctionService, ApplicationDbContext ctx) : ControllerBase
{
    private const string AuctionImagesBasePath = "data/auctions";

    public record CreateAuctionRequest
    {
        public required string Title { get; init; }
        public required string Description { get; init; }
        public required decimal MinimumPrice { get; init; }
        public required DateTime ClosureTime { get; init; }
    }

    public record UpdateAuctionRequest
    {
        public string? Title { get; init; }
        public string? Description { get; init; }
        public decimal? MinimumPrice { get; init; }
        public DateTime? ClosureTime { get; init; }
    }

    public record ImageDto
    {
        public Guid Id { get; init; }
        public string Url { get; init; } = "";
    }

    [HttpPost]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> CreateAuction(CancellationToken ct)
    {
        CreateAuctionRequest request;
        List<IFormFile> files;

        if (Request.ContentType?.StartsWith("multipart/") == true ||
            Request.ContentType?.Contains("form") == true)
        {
            var form = await Request.ReadFormAsync();
            request = new CreateAuctionRequest
            {
                Title = form["title"].ToString(),
                Description = form["description"].ToString(),
                MinimumPrice = decimal.Parse(form["minimumPrice"].ToString(), CultureInfo.InvariantCulture),
                ClosureTime = DateTime.Parse(form["closureTime"].ToString(), CultureInfo.InvariantCulture),
            };
            files = form.Files.ToList();
        }
        else
        {
            var body = await Request.ReadFromJsonAsync<CreateAuctionRequest>();
            if (body is null) return BadRequest();
            request = body;
            files = [];
        }

        var auctionId = Guid.NewGuid();

        var createdEvent = new AuctionCreated
        {
            AuctionId = auctionId,
            Title = request.Title,
            Description = request.Description,
            MinimumPrice = request.MinimumPrice,
            ClosureTime = request.ClosureTime,
        };

        var store = new DbEventStore(ctx);
        var handler = new IncomingEventHandler<AuctionCreated, Guid>(store, new CreateAuctionEvaluator(), new EventHandlerOptions());

        try
        {
            await handler.Execute(createdEvent, ct);
        }
        catch (InvariantViolation ex)
        {
            return BadRequest(ex.Message);
        }

        if (files.Count > 0)
        {
            await StoreAndAddImages(auctionId, files, ct);
        }

        return Ok(auctionId);
    }

    [HttpPatch("{id}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> UpdateAuction(Guid id, [FromBody] UpdateAuctionRequest request, CancellationToken ct)
    {
        var updatedEvent = new AuctionUpdated
        {
            AuctionId = id,
            Title = request.Title,
            Description = request.Description,
            MinimumPrice = request.MinimumPrice,
            ClosureTime = request.ClosureTime,
        };

        var store = new DbEventStore(ctx);
        var handler = new IncomingEventHandler<AuctionUpdated, bool>(store, new UpdateAuctionEvaluator(), new EventHandlerOptions());

        try
        {
            await handler.Execute(updatedEvent, ct);
        }
        catch (InvariantViolation ex)
        {
            return BadRequest(ex.Message);
        }

        return Ok();
    }

    [HttpPost("{id}/images")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> UploadImages(Guid id, [FromForm] IFormFileCollection files, CancellationToken ct)
    {
        if (files.Count == 0)
            return BadRequest("No files uploaded.");

        await StoreAndAddImages(id, files.ToList(), ct);

        return Ok();
    }

    [HttpDelete("{id}/images/{imageId}")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> RemoveImage(Guid id, Guid imageId, CancellationToken ct)
    {
        var removedEvent = new AuctionImagesRemoved
        {
            AuctionId = id,
            ImageIds = [imageId],
        };

        var store = new DbEventStore(ctx);
        var handler = new IncomingEventHandler<AuctionImagesRemoved, bool>(store, new RemoveImagesEvaluator(), new EventHandlerOptions());

        try
        {
            await handler.Execute(removedEvent, ct);
        }
        catch (InvariantViolation ex)
        {
            return BadRequest(ex.Message);
        }

        var filePath = Path.Combine(AuctionImagesBasePath, id.ToString(), $"{imageId}.bin");
        if (System.IO.File.Exists(filePath))
            System.IO.File.Delete(filePath);

        return Ok();
    }

    [HttpPost("{id}/close")]
    [Authorize(Policy = "IsAdmin")]
    public async Task<IActionResult> CloseAuction(Guid id, CancellationToken ct)
    {
        var closedEvent = new AuctionClosed
        {
            AuctionId = id,
            Reason = AuctionCloseReason.Manual,
        };

        var store = new DbEventStore(ctx);
        var handler = new IncomingEventHandler<AuctionClosed, bool>(store, new CloseAuctionEvaluator(), new EventHandlerOptions());

        try
        {
            await handler.Execute(closedEvent, ct);
        }
        catch (InvariantViolation ex)
        {
            return BadRequest(ex.Message);
        }

        return Ok();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ListAuctions(CancellationToken ct)
    {
        var auctions = await auctionService.ListAuctionSummaries();
        return Ok(auctions);
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetAuction(Guid id, CancellationToken ct)
    {
        var summary = await auctionService.GetAuctionSummary(id);
        if (summary is null)
            return NotFound();

        return Ok(summary);
    }

    [HttpGet("{auctionId}/images/{imageId}")]
    [Authorize]
    public async Task<IActionResult> ServeImage(Guid auctionId, Guid imageId)
    {
        var filePath = Path.Combine(AuctionImagesBasePath, auctionId.ToString(), $"{imageId}.bin");
        if (!System.IO.File.Exists(filePath))
            return NotFound();

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/octet-stream");
    }

    private async Task StoreAndAddImages(Guid auctionId, List<IFormFile> files, CancellationToken ct)
    {
        var auctionDir = Path.Combine(AuctionImagesBasePath, auctionId.ToString());
        Directory.CreateDirectory(auctionDir);

        var imageRefs = new List<AuctionImageRef>();

        foreach (var file in files)
        {
            var imageId = Guid.NewGuid();
            var filePath = Path.Combine(auctionDir, $"{imageId}.bin");

            await using var stream = file.OpenReadStream();
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, ct);
            var hash = Convert.ToHexString(hashBytes);

            stream.Position = 0;
            await using var fileStream = System.IO.File.Create(filePath);
            await stream.CopyToAsync(fileStream, ct);

            imageRefs.Add(new AuctionImageRef
            {
                Id = imageId,
                FilePath = filePath,
                Hash = hash,
            });
        }

        var addedEvent = new AuctionImagesAdded
        {
            AuctionId = auctionId,
            Images = imageRefs,
        };

        var store = new DbEventStore(ctx);
        var handler = new IncomingEventHandler<AuctionImagesAdded, bool>(store, new AddImagesEvaluator(), new EventHandlerOptions());

        try
        {
            await handler.Execute(addedEvent, ct);
        }
        catch (InvariantViolation ex)
        {
            throw new InvalidOperationException($"Failed to add images: {ex.Message}", ex);
        }
    }
}
