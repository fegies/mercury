using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace appcore.Entities;

/// <summary>
/// The user entity
/// </summary>
[Index(nameof(OidIss), nameof(OidSub), IsUnique = true)]
public class UserEntity
{
    /// <summary>
    /// An internal id, only valid in this app
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// the oidc issuer that identifies this user
    /// </summary>
    public required string OidIss { get; set; }
    /// <summary>
    /// the oidc sub claim for this user. Only unique in combination with the issuer
    /// </summary>
    public required string OidSub { get; set; }

    /// <summary>
    /// User name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// User email
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Profile picture url. May point to an external source or an internal endpoint.
    /// </summary>
    public string? ProfilePictureUrl { get; set; }
}
