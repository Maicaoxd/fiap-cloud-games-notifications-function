using System.Text.Json;
using FiapCloudGames.Contracts.Events;

namespace FiapCloudGames.NotificationsFunction.Messaging;

public sealed record ReceivedMessage<T>(Guid MessageId, T Message);

public static class MassTransitEnvelopeReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ReceivedMessage<UserCreatedEvent> ReadUserCreated(string json)
    {
        var result = Read<UserCreatedEvent>(json);
        var message = result.Message;
        if (message.UserId == Guid.Empty || string.IsNullOrWhiteSpace(message.Name) ||
            string.IsNullOrWhiteSpace(message.Email) || message.CreatedAt == default)
            throw new JsonException("UserCreatedEvent possui valores obrigatorios ausentes.");
        return result;
    }

    public static ReceivedMessage<PaymentProcessedEvent> ReadPaymentProcessed(string json)
    {
        var result = Read<PaymentProcessedEvent>(json);
        var message = result.Message;
        if (message.OrderId == Guid.Empty || message.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(message.Status) || message.ProcessedAt == default ||
            message.TotalPrice < 0 || message.Games is null || message.Games.Count == 0 ||
            message.Games.Any(game => game is null || game.GameId == Guid.Empty || game.Price < 0))
            throw new JsonException("PaymentProcessedEvent possui valores obrigatorios invalidos.");
        return result;
    }

    private static ReceivedMessage<T> Read<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException("O envelope da mensagem esta vazio.");
        using var document = JsonDocument.Parse(json);
        var envelope = document.RootElement;
        if (envelope.ValueKind != JsonValueKind.Object)
            throw new JsonException("O envelope da mensagem deve ser um objeto JSON.");

        if (!envelope.TryGetProperty("messageId", out var id) ||
            id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var messageId) ||
            messageId == Guid.Empty)
            throw new JsonException("O envelope da mensagem exige um messageId valido.");

        var expectedType = $"urn:message:{typeof(T).Namespace}:{typeof(T).Name}";
        if (!envelope.TryGetProperty("messageType", out var types) ||
            types.ValueKind != JsonValueKind.Array ||
            !types.EnumerateArray().Any(type => type.ValueKind == JsonValueKind.String &&
                string.Equals(type.GetString(), expectedType, StringComparison.Ordinal)))
            throw new JsonException("O envelope nao identifica o tipo de evento esperado.");

        if (!envelope.TryGetProperty("message", out var payload) ||
            payload.ValueKind != JsonValueKind.Object)
            throw new JsonException("O envelope exige um objeto no campo message.");

        // Detect missing constructor fields rather than accepting default Guid/decimal values.
        var required = typeof(T) == typeof(UserCreatedEvent)
            ? new[] { "userId", "name", "email", "createdAt" }
            : new[] { "orderId", "userId", "games", "totalPrice", "status", "processedAt" };
        if (required.Any(field => !payload.TryGetProperty(field, out var value) ||
            value.ValueKind == JsonValueKind.Null))
            throw new JsonException("Os dados do evento possuem campos obrigatorios ausentes.");

        var message = payload.Deserialize<T>(Options)
            ?? throw new JsonException("Os dados do evento estao nulos.");
        return new ReceivedMessage<T>(messageId, message);
    }
}
