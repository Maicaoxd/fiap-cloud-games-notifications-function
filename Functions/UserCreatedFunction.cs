using FiapCloudGames.NotificationsFunction.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FiapCloudGames.NotificationsFunction.Functions;

public sealed class UserCreatedFunction(ILogger<UserCreatedFunction> logger)
{
    [Function(nameof(UserCreatedFunction))]
    public void Run(
        [RabbitMQTrigger("%UserCreatedQueue%", ConnectionStringSetting = "RabbitMQConnection")] string envelope)
    {
        var received = MassTransitEnvelopeReader.ReadUserCreated(envelope);
        var message = received.Message;
        logger.LogInformation(
            "E-mail de boas-vindas enviado para {Name} ({Email}). UserId: {UserId}, MessageId: {MessageId} (simulacao).",
            message.Name, message.Email, message.UserId, received.MessageId);
    }
}
