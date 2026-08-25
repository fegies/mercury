using System;

namespace appcore.Entities.Events;

public class UserRoleChanged : StoredEvent
{
    public required Guid UserId { get; init; }

    public string? Role { get; init; }
}
