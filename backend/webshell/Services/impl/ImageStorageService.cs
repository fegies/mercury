namespace backend.Services;

class ImageStorageService(
) : IImageStorage
{
    private const string BasePath = "data/images";

    public Task<Guid> StoreAsync(string area, byte[] bytes, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var filePath = FilePathFor(area, id);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllBytes(filePath, bytes);
        return Task.FromResult(id);
    }

    public Task<byte[]?> ReadAsync(string area, Guid id, CancellationToken ct)
    {
        var filePath = FilePathFor(area, id);
        if (!File.Exists(filePath))
            return Task.FromResult<byte[]?>(null);
        return Task.FromResult<byte[]?>(File.ReadAllBytes(filePath));
    }

    public Task DeleteAsync(string area, Guid id, CancellationToken ct)
    {
        var filePath = FilePathFor(area, id);
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    private static string FilePathFor(string area, Guid id)
        => Path.Combine(BasePath, area, $"{id}.bin");
}
