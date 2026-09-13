namespace FiapCloudGames.Contracts.Events;

public sealed record UserCreatedEvent(
    Guid UserId,
    string Name,
    string Email,
    DateTime CreatedAt);
