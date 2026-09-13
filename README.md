# FIAP Cloud Games — Notifications Function

Azure Functions v4 com worker isolado .NET 10 para consumir eventos do RabbitMQ e simular notificações por e-mail nos logs. Não envia e-mails reais nem cria recursos na Azure durante a execução local.

## Requisitos

- Docker Desktop com containers Linux para executar pelo Compose.
- .NET 10 SDK para compilar e testar.
- Azure Functions Core Tools v4 com suporte a .NET 10 para executar fora do container.

## Executar com Docker

Na raiz deste repositório:

```powershell
docker compose -f docker-compose.dev.yml up -d --build
docker compose -f docker-compose.dev.yml ps -a
docker compose -f docker-compose.dev.yml logs -f notifications-function
```

O ambiente inclui a Function, RabbitMQ, Azurite e um inicializador idempotente das filas e bindings. O inicializador usa PowerShell 7.4 e executa rabbitmq/configure-rabbitmq.ps1 com as definições de rabbitmq/definitions.json. As configurações vêm do Compose, não de local.settings.json.

| Componente | Acesso local |
| --- | --- |
| RabbitMQ AMQP | localhost:5673 |
| RabbitMQ Management | http://localhost:15673 — guest / guest |
| Host da Function | http://localhost:7072 |
| Azurite — Blob, Queue e Table | localhost:10010, 10011 e 10012 |

A Function não possui endpoints HTTP de negócio. O host HTTP é operacional. O serviço rabbitmq-topology deve terminar com código 0; os demais permanecem em execução.

Para parar preservando os dados:

```powershell
docker compose -f docker-compose.dev.yml down
```

## Executar o fluxo completo

Mantenha este repositório ao lado de fiap-cloud-games-orchestration e dos repositórios UsersAPI, CatalogAPI, PaymentsAPI e NotificationsAPI. Na raiz da orquestração:

```powershell
docker compose up -d --build
docker compose logs -f notifications-function
```

Esse ambiente contém as APIs, bancos, Gateway, RabbitMQ, Function e Azurite. Não é necessário iniciar outro host com dotnet run.

1. Cadastre um usuário em POST http://localhost:8000/identity/users.
2. Confira a simulação de boas-vindas nos logs.
3. Faça login em POST /identity/auth/login e use o JWT como Bearer token.
4. Consulte GET /catalog/games e escolha um jogo ativo.
5. Faça POST /catalog/library/games/purchase com o corpo abaixo.
6. Confira a confirmação de compra nos logs e consulte GET /catalog/library/games. O processamento é assíncrono.

```json
{
  "gameIds": ["GUID_DO_JOGO"]
}
```

O Compose independente usa outro broker e não recebe automaticamente os eventos da orquestração.

## Executar no Kubernetes local

Na raiz do repositório fiap-cloud-games-orchestration, confirme o contexto local e aplique a base:

```powershell
kubectl config current-context
kubectl apply -k .\k8s
kubectl rollout status deployment/azurite -n fiap-cloud-games --timeout=300s
kubectl rollout status deployment/notifications-function -n fiap-cloud-games --timeout=600s
kubectl logs deployment/notifications-function -c notifications-function -n fiap-cloud-games --follow
```

Os initContainers aguardam o Azurite e executam configure-rabbitmq.ps1 antes de iniciar os triggers. O ConfigMap é gerado diretamente dos arquivos desta pasta rabbitmq, sem cópias do configurador. O armazenamento do emulador usa um PVC de 1 GiB; os volumes Docker e Kubernetes são independentes.

notifications-function-secret fornece RabbitMQConnection e AzureWebJobsStorage. As configurações precisam corresponder a rabbitmq-secret e azurite-secret. A base mantém todos os endpoints internos e não cria recursos na Azure nem configura Application Insights.

Para testar pelo Kong, em outro terminal:

```powershell
kubectl port-forward svc/kong-proxy 8005:8000 --address 127.0.0.1 -n fiap-cloud-games
```

Use http://localhost:8005 para cadastro, login, compra e consulta da biblioteca, como no fluxo Docker. Para consultar o configurador, use kubectl logs deployment/notifications-function -c configure-rabbitmq -n fiap-cloud-games.

NotificationsAPI não faz parte da base padrão. Em um cluster existente, reduza seu Deployment a zero antes de iniciar a Function. A implementação legada permanece disponível em k8s/notifications-api da orquestração; pare a Function antes de aplicá-la.

## Executar fora do container

Copie local.settings.example.json para local.settings.json somente se o arquivo de destino ainda não existir. O arquivo local é ignorado pelo Git.

| Chave em Values | Configuração |
| --- | --- |
| FUNCTIONS_WORKER_RUNTIME | dotnet-isolated |
| AzureWebJobsStorage | UseDevelopmentStorage=true para Azurite nas portas padrão 10000–10002 |
| RabbitMQConnection | URI AMQP do broker local, incluindo usuário, senha e virtual host |
| UserCreatedQueue | notifications-user-created-event |
| PaymentProcessedQueue | notifications-payment-processed-event |
| AzureWebJobs.UserCreatedFunction.Disabled | false para habilitar boas-vindas |
| AzureWebJobs.PaymentProcessedFunction.Disabled | false para habilitar confirmação de pagamento |

Escape caracteres especiais dos componentes da URI AMQP. O exemplo versionado deixa os triggers desabilitados. As filas e os bindings precisam existir antes de iniciar o host; use um broker provisionado por um dos Composes.

Azurite deve estar em execução. UseDevelopmentStorage=true não aponta para as portas alternativas do Compose deste repositório. Para usar esse emulador, configure uma cadeia de conexão com a conta fictícia fcglocal, a chave local do Compose e os endpoints localhost:10010, 10011 e 10012.

Pare qualquer outro consumidor das mesmas filas antes de executar:

```powershell
dotnet run
```

Encerre o host com Ctrl+C. Não configure Application Insights nem publique recursos na Azure para executar o exemplo local.

## Eventos e estrutura

| Function | Evento | Fila | Comportamento |
| --- | --- | --- | --- |
| UserCreatedFunction | UserCreatedEvent | notifications-user-created-event | Registra nome, e-mail, UserId e MessageId na simulação de boas-vindas |
| PaymentProcessedFunction | PaymentProcessedEvent | notifications-payment-processed-event | Registra pedido, usuário, quantidade de jogos, total e MessageId; confirma somente Approved |

Contracts/Events mantém os contratos no namespace FiapCloudGames.Contracts.Events. Messaging/MassTransitEnvelopeReader valida messageId, messageType e os campos obrigatórios de message no envelope MassTransit.

Os logs contêm dados pessoais. Use dados sintéticos ao estudar o projeto e controle acesso e retenção. Credenciais e envelopes completos não são registrados.

## Testes

```powershell
dotnet build
dotnet test tests/NotificationsFunction.Tests/NotificationsFunction.Tests.csproj
```

Os testes cobrem envelopes inválidos, validação dos eventos, rastreabilidade e notificações de pagamentos aprovados e rejeitados.

## Publicar a imagem no Docker Hub

A imagem publicada é maicaoxd/fiap-cloud-games-notifications-function:0.1.0, para Linux amd64. Para publicar no seu próprio namespace, substitua maicaoxd nos comandos e no Compose.

```powershell
docker login
docker build --platform linux/amd64 -t maicaoxd/fiap-cloud-games-notifications-function:0.1.0 .
docker push maicaoxd/fiap-cloud-games-notifications-function:0.1.0
```

A publicação da imagem no Docker Hub não cria recursos na Azure. Os parâmetros RabbitMQ e Storage são fornecidos somente na execução, por variáveis de ambiente.

## Consumo e tratamento de erros

A confirmação de consumo é gerenciada pelo binding RabbitMQ. Eventos inválidos lançam JsonException, sem registrar sucesso. Retries, filas _error e Fault<T> do MassTransit não são herdados pelo binding; políticas de reentrega e DLX/DLQ dependem da configuração do broker.

Não há deduplicação persistente: uma reentrega pode repetir a notificação simulada. Não execute NotificationsAPI e Function simultaneamente nas mesmas filas, pois elas disputarão as mensagens.

O Dockerfile usa a imagem oficial do host Azure Functions. Arquivos local.settings*, .env*, certificados, testes e Git são excluídos da imagem. Azurite usa armazenamento local com volume; esse ambiente não demonstra autoscaling ou scale-to-zero em nuvem.

Referências: [trigger RabbitMQ](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-rabbitmq-trigger) e [envelope MassTransit](https://masstransit.io/documentation/configuration/serialization).
