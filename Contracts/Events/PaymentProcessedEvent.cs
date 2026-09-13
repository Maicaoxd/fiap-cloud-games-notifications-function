namespace FiapCloudGames.Contracts.Events;

public sealed record PaymentProcessedEvent(
    Guid OrderId,
    Guid UserId,
    IReadOnlyCollection<PaymentProcessedGameEventItem> Games,
    decimal TotalPrice,
    string Status,
    DateTime ProcessedAt);

public sealed record PaymentProcessedGameEventItem(Guid GameId, decimal Price);
