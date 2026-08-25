using System;

namespace appcore.Entities.Events;

public class UserCreated : StoredEvent
{
    public required Guid UserId { get; init; }
    public required string OidIss { get; init; }
    public required string OidSub { get; init; }
    public required string Name { get; init; }
    public string? Email { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public string? Role { get; init; }
}
