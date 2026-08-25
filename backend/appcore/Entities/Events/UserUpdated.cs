using System;

namespace appcore.Entities.Events;

public class UserUpdated : StoredEvent
{
    public required Guid UserId { get; init; }

    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? ProfilePictureUrl { get; init; }
}
