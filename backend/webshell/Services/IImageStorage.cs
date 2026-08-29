namespace backend.Services;

/// <summary>
/// Stores and retrieves binary files on local disk, namespaced by area and id.
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Streams the given source to disk and returns the id under which it can be retrieved.
    /// </summary>
    Task<Guid> StoreAsync(string area, Stream source, CancellationToken ct);

    /// <summary>
    /// Returns the full on-disk path for the given area and id, or null when absent.
    /// </summary>
    string? ResolvePath(string area, Guid id);

    /// <summary>
    /// Deletes the file for the given area and id, if present.
    /// </summary>
    Task DeleteAsync(string area, Guid id, CancellationToken ct);
}
