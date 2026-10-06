using System;
using System.ComponentModel.DataAnnotations;
using appcore.Configuration;

namespace backend.Configuration;

/// <summary>
/// A structured class describing the configuration
/// </summary>
public class BackendConfig
{
    /// <summary>
    /// OIDC configuration options
    /// </summary>
    public OidcConfigurationValue OidcConfig { get; init; } = new();

    /// <summary>
    /// Microsoft Entra ID provider options
    /// </summary>
    public EntraConfigurationValue EntraConfig { get; init; } = new();

    /// <summary>
    /// Auction domain options
    /// </summary>
    public AuctionConfig AuctionConfig { get; init; } = new();

    /// <summary>
    /// Image storage options
    /// </summary>
    public ImageStorageConfigurationValue ImageStorage { get; init; } = new();

    /// <summary>
    /// Forwarded headers (reverse proxy) options
    /// </summary>
    public ForwardedConfigurationValue ForwardedConfig { get; init; } = new();

    internal void Validate()
    {
        var ctx = new ValidationContext(this);
        Validator.ValidateObject(this, ctx, true);
        OidcConfig.Validate();

        // Validation attributes are not evaluated on nested config objects
        // (Validator.ValidateObject does not recurse), so nested rules are
        // checked explicitly here.
        if (string.IsNullOrWhiteSpace(ImageStorage.BasePath))
            throw new ValidationException("ImageStorage:BasePath must not be empty.");

        foreach (var proxy in ForwardedConfig.TrustedProxies)
        {
            if (!System.Net.IPAddress.TryParse(proxy, out _))
                throw new ValidationException(
                    $"ForwardedConfig:TrustedProxies contains an invalid IP address: '{proxy}'");
        }
    }
}
