using FiapCloudGames.NotificationsFunction.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FiapCloudGames.NotificationsFunction.Functions;

public sealed class PaymentProcessedFunction(ILogger<PaymentProcessedFunction> logger)
{
    [Function(nameof(PaymentProcessedFunction))]
    public void Run(
        [RabbitMQTrigger("%PaymentProcessedQueue%", ConnectionStringSetting = "RabbitMQConnection")] string envelope)
    {
        var received = MassTransitEnvelopeReader.ReadPaymentProcessed(envelope);
        var message = received.Message;
        if (!string.Equals(message.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Notificacao de compra nao enviada. OrderId: {OrderId}, UserId: {UserId}, Status: {Status}, MessageId: {MessageId}",
                message.OrderId, message.UserId, message.Status, received.MessageId);
            return;
        }
        logger.LogInformation(
            "E-mail de confirmacao de compra enviado. OrderId: {OrderId}, UserId: {UserId}, Jogos: {GameCount}, Total: {TotalPrice}, MessageId: {MessageId} (simulacao).",
            message.OrderId, message.UserId, message.Games.Count, message.TotalPrice, received.MessageId);
    }
}
