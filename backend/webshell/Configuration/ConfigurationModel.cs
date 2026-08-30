using System;
using System.ComponentModel.DataAnnotations;

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

    internal void Validate()
    {
        var ctx = new ValidationContext(this);
        Validator.ValidateObject(this, ctx, true);
        OidcConfig.Validate();
    }
}
