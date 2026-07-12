using System.ComponentModel.DataAnnotations;

namespace backend.Configuration;

/// <summary>
/// OIDC configuration settings
/// </summary>
public class OidcConfigurationValue
{
    /// <summary>
    /// The issuer url of the oidc provider
    /// Should be the root (without the .well-known/.... suffix)
    /// </summary>
    [Required]
    public string? AuthorityUrl { get; init; }

    /// <summary>
    /// The client id this application is registered as
    /// </summary>
    [Required]
    public string? ClientId { get; init; }

    /// <summary>
    /// The client secret of this app registration
    /// </summary>
    [Required]
    public string? ClientSecret { get; init; }

    /// <summary>
    /// provider type
    /// </summary>
    public enum ProviderTypeValue
    {
        /// <summary>
        /// This is a zitadel provider
        /// </summary>
        Zitadel,
        /// <summary>
        /// This is microsoft entra id
        /// </summary>
        Entra,
        /// <summary>
        /// This is a generic oidc compliant provider
        /// </summary>
        Generic
    }

    /// <summary>
    /// What type of provider is this?
    /// </summary>
    [EnumDataType(typeof(ProviderTypeValue))]
    public ProviderTypeValue ProviderType { get; init; } = ProviderTypeValue.Generic;

    internal void Validate()
    {
        Validator.ValidateObject(this, new ValidationContext(this), true);
    }
}