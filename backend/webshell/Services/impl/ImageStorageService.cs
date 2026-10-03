namespace backend.Services;

class ImageStorageService : IImageStorage
{
    private readonly string basePath;

    public ImageStorageService(string basePath)
    {
        this.basePath = Path.GetFullPath(basePath);
    }

    public async Task<Guid> StoreAsync(string area, Stream source, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var filePath = FilePathFor(area, id);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var fileStream = File.Create(filePath);
        await source.CopyToAsync(fileStream, ct);
        return id;
    }

    public string? ResolvePath(string area, Guid id)
    {
        var filePath = FilePathFor(area, id);
        return File.Exists(filePath) ? filePath : null;
    }

    public Task DeleteAsync(string area, Guid id, CancellationToken ct)
    {
        var filePath = FilePathFor(area, id);
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        return Task.CompletedTask;
    }

    private string FilePathFor(string area, Guid id)
        => Path.Combine(basePath, area, $"{id}.bin");
}
