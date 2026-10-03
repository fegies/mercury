namespace backend.Configuration;

/// <summary>
/// Image storage configuration settings
/// </summary>
public class ImageStorageConfigurationValue
{
    /// <summary>
    /// Directory where uploaded images (auction photos, profile pictures) are stored.
    /// Relative paths are resolved against the process working directory.
    /// </summary>
    public string? BasePath { get; init; } = "data/images";
}
