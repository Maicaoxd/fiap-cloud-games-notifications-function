using System.Text.Json;
using FiapCloudGames.Contracts.Events;
using FiapCloudGames.NotificationsFunction.Functions;
using FiapCloudGames.NotificationsFunction.Messaging;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace NotificationsFunction.Tests;

public sealed class NotificationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly Guid UserId = Guid.NewGuid();
    private static string Envelope<T>(T message, string? type = null) => JsonSerializer.Serialize(new
    {
        messageId = Guid.NewGuid(),
        messageType = new[] { type ?? $"urn:message:{typeof(T).Namespace}:{typeof(T).Name}" },
        message
    }, Options);
    private static UserCreatedEvent User() => new(UserId, "Fixture", "fixture@example.invalid", DateTime.UtcNow);
    private static PaymentProcessedEvent Payment(string status = "Approved") => new(
        Guid.NewGuid(), UserId, [new(Guid.NewGuid(), 10)], 10, status, DateTime.UtcNow);

    [Fact]
    public void ReadsUserAndPreservesMessageId()
    {
        var json = Envelope(User());
        var result = MassTransitEnvelopeReader.ReadUserCreated(json);
        Assert.Equal(UserId, result.Message.UserId);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(document.RootElement.GetProperty("messageId").GetGuid(), result.MessageId);
    }

    [Fact]
    public void ReadsPayment() => Assert.Equal(10, MassTransitEnvelopeReader.ReadPaymentProcessed(Envelope(Payment())).Message.TotalPrice);

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    public void RejectsMalformedEnvelope(string json) => Assert.ThrowsAny<JsonException>(() => MassTransitEnvelopeReader.ReadUserCreated(json));

    [Fact]
    public void RejectsWrongType() => Assert.Throws<JsonException>(() => MassTransitEnvelopeReader.ReadUserCreated(Envelope(User(), "urn:message:Other:UserCreatedEvent")));

    [Theory]
    [InlineData("messageId")]
    [InlineData("messageType")]
    [InlineData("message")]
    public void RejectsMissingEnvelopeField(string field)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(Envelope(User()))!.AsObject();
        root.Remove(field);
        Assert.Throws<JsonException>(() => MassTransitEnvelopeReader.ReadUserCreated(root.ToJsonString()));
    }

    [Theory]
    [InlineData("userId")]
    [InlineData("name")]
    [InlineData("email")]
    [InlineData("createdAt")]
    public void RejectsMissingUserField(string field)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(Envelope(User()))!;
        root["message"]!.AsObject().Remove(field);
        Assert.Throws<JsonException>(() => MassTransitEnvelopeReader.ReadUserCreated(root.ToJsonString()));
    }

    [Fact]
    public void RejectsInvalidUser() => Assert.Throws<JsonException>(() => MassTransitEnvelopeReader.ReadUserCreated(Envelope(User() with { UserId = Guid.Empty })));

    [Fact]
    public void RejectsInvalidPayment() => Assert.Throws<JsonException>(() => MassTransitEnvelopeReader.ReadPaymentProcessed(Envelope(Payment() with { Games = [], TotalPrice = -1 })));

    [Fact]
    public void WelcomeLogsEventFieldsLikeOriginalConsumer()
    {
        var logger = Substitute.For<ILogger<UserCreatedFunction>>();
        var user = User();
        new UserCreatedFunction(logger).Run(Envelope(user));
        var log = Assert.Single(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == "Log");
        var text = log.GetArguments()[2]!.ToString()!;
        Assert.Contains("E-mail de boas-vindas enviado para", text);
        Assert.Contains(user.Email, text);
        Assert.Contains(user.Name, text);
        Assert.Contains(user.UserId.ToString(), text);
        Assert.Contains("(simulacao)", text);
    }

    [Theory]
    [InlineData("Approved", true)]
    [InlineData("approved", true)]
    [InlineData("Rejected", false)]
    public void PurchaseDependsOnApproval(string status, bool approved)
    {
        var logger = Substitute.For<ILogger<PaymentProcessedFunction>>();
        var payment = Payment(status);
        new PaymentProcessedFunction(logger).Run(Envelope(payment));
        var log = Assert.Single(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == "Log");
        var text = log.GetArguments()[2]!.ToString()!;
        Assert.Contains(approved ? "E-mail de confirmacao de compra enviado" : "Notificacao de compra nao enviada", text);
        Assert.Contains(payment.OrderId.ToString(), text);
        Assert.Contains(payment.UserId.ToString(), text);
        if (approved)
        {
            Assert.Contains("Jogos: 1", text);
            Assert.Contains("Total: 10", text);
            Assert.Contains("(simulacao)", text);
        }
        else Assert.Contains($"Status: {status}", text);
    }

    [Fact]
    public void InvalidMessageDoesNotLogSuccess()
    {
        var logger = Substitute.For<ILogger<UserCreatedFunction>>();
        Assert.Throws<JsonException>(() => new UserCreatedFunction(logger).Run("{}"));
        Assert.Empty(logger.ReceivedCalls());
    }
}
