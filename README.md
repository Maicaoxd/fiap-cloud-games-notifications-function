# FIAP Cloud Games — Notifications Function

Azure Functions v4, worker isolado .NET 10. Duas Functions RabbitMQ substituem futuramente os consumers NotificationsAPI. Nesta etapa tudo e local: nenhum recurso Azure, exportador Azure Monitor ou envio real de email.

## Estrutura

- Contracts/Events: contratos identicos aos produtores, namespace FiapCloudGames.Contracts.Events.
- Messaging/MassTransitEnvelopeReader: valida messageId, URN em messageType e campos de message. Preserva o ID para rastreabilidade.
- Functions/UserCreatedFunction: simula boas-vindas com nome, email e UserId, como o consumer original. Logs contem dados pessoais: use dados sinteticos nos testes e restrinja acesso/retencao em producao. Nao registra o envelope completo ou credenciais.
- Functions/PaymentProcessedFunction: simula confirmacao somente para Approved, sem diferenciar maiusculas.
- local.settings.example.json: exemplo SEM credenciais reais e com ambas Functions desabilitadas por seguranca.
- tests/NotificationsFunction.Tests: validacao dos envelopes e comportamento das notificacoes.

## Compilar e testar sem consumir filas

```powershell
dotnet build
dotnet test tests/NotificationsFunction.Tests/NotificationsFunction.Tests.csproj
```

## Configurar localmente (checkpoint seguinte)

Preserve seu local.settings.json existente; transfira manualmente as chaves do exemplo, sem sobrescrever configuracoes pessoais. Configure RabbitMQConnection com usuario e senha do RabbitMQ LOCAL. Escape caracteres especiais dos componentes da URI. AzureWebJobsStorage deve ser UseDevelopmentStorage=true e Azurite deve estar rodando localmente. Nao configure APPLICATIONINSIGHTS_CONNECTION_STRING.

As filas precisam existir com seus bindings MassTransit. O trigger consome a fila, mas nao reproduz a topologia dos eventos criada pelo MassTransit. Antes da primeira troca, suba NotificationsAPI para criar a topologia se necessario e confirme as duas filas no RabbitMQ Management.

Somente quando formos testar juntos:

1. Pare NotificationsAPI apenas no mesmo ambiente RabbitMQ usado pela Function. Nao apague a API ou as filas. Se estiver usando Docker local, isso nao exige parar NotificationsAPI do Kubernetes, pois sao brokers distintos.
2. Habilite primeiro AzureWebJobs.UserCreatedFunction.Disabled=false. Mantenha pagamento desabilitado ate o segundo teste.
3. Inicie Azurite e execute func start. Essa porta HTTP do host e administrativa/local, nao uma nova rota da aplicacao no Kong.
4. Cadastre um usuario de teste pela UsersAPI/Kong e confirme E-mail de boas-vindas enviado para, com indicacao de simulacao, e consumo da fila.
5. Habilite a Function de pagamento e teste uma compra aprovada e um evento de pagamento rejeitado.
6. Para voltar: Ctrl+C na Function antes de religar NotificationsAPI. Nunca execute ambos consumindo a mesma fila se espera que a Function receba todos os eventos.

Nao executamos esses passos automaticamente. Nao use func azure functionapp publish ou crie Storage Account/Application Insights para esta etapa.

## Erros, reentrega e limites

JSON/envelope/evento invalido lanca JsonException; nao ha log de sucesso nem captura que transforme erro em sucesso. A confirmacao de consumo e gerenciada pelo binding RabbitMQ. Os retries, _error e Fault<T> do MassTransit NAO sao herdados. DLX/DLQ devem ser configurados previamente no broker, e tratamento de erro/reentrega deve ser validado com mensagem sintetica em fila isolada antes da troca definitiva. Nao publique mensagens invalidas nas filas existentes para testar.

Nao ha deduplicacao persistente: reentrega pode repetir o log simulado. Um envio real exigira idempotencia duravel por messageId e estrategia de retries/erros. Nao foi introduzido envio de email real.

Rodar o host local ou um container demonstra Functions localmente, nao comprova autoscaling/scale-to-zero serverless em nuvem. Publicacao RabbitMQ no Azure atualmente requer planos Premium/Dedicated, portanto nao deve ser tratada como gratuita.

Referencias: [binding RabbitMQ](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-rabbitmq-trigger) e [envelope MassTransit](https://masstransit.io/documentation/configuration/serialization).
