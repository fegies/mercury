namespace backend.Services;

/// <summary>
/// Stores and retrieves binary files on local disk, namespaced by area and id.
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Stores the given bytes and returns the id under which they can be retrieved.
    /// </summary>
    Task<Guid> StoreAsync(string area, byte[] bytes, CancellationToken ct);

    /// <summary>
    /// Reads the bytes stored for the given area and id, or null when absent.
    /// </summary>
    Task<byte[]?> ReadAsync(string area, Guid id, CancellationToken ct);

    /// <summary>
    /// Deletes the file for the given area and id, if present.
    /// </summary>
    Task DeleteAsync(string area, Guid id, CancellationToken ct);
}
