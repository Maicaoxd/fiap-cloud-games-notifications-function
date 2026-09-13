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

As filas precisam existir com seus bindings MassTransit. O trigger consome a fila, mas nao reproduz a topologia dos eventos. Os Composes agora provisionam isso por rabbitmq/definitions.json e rabbitmq/configure.py, sem iniciar a NotificationsAPI antiga. Em execucao avulsa com dotnet run, use o broker ja provisionado pela orquestracao ou pelo Compose de desenvolvimento.

Somente quando formos testar juntos:

1. Pare NotificationsAPI apenas no mesmo ambiente RabbitMQ usado pela Function. Nao apague a API ou as filas. Se estiver usando Docker local, isso nao exige parar NotificationsAPI do Kubernetes, pois sao brokers distintos.
2. Habilite primeiro AzureWebJobs.UserCreatedFunction.Disabled=false. Mantenha pagamento desabilitado ate o segundo teste.
3. Inicie Azurite e execute dotnet run. Core Tools 4.14 recomenda esse comando para carregar extensoes a partir da pasta de saida correta. Se o terminal ainda nao atualizou o PATH, reabra-o antes. A porta HTTP do host e administrativa/local, nao uma nova rota da aplicacao no Kong.
4. Cadastre um usuario de teste pela UsersAPI/Kong e confirme E-mail de boas-vindas enviado para, com indicacao de simulacao, e consumo da fila.
5. Habilite a Function de pagamento e teste uma compra aprovada e um evento de pagamento rejeitado.
6. Para voltar: Ctrl+C na Function antes de religar NotificationsAPI. Nunca execute ambos consumindo a mesma fila se espera que a Function receba todos os eventos.

Nao executamos esses passos automaticamente. Nao use func azure functionapp publish ou crie Storage Account/Application Insights para esta etapa.

## Erros, reentrega e limites

JSON/envelope/evento invalido lanca JsonException; nao ha log de sucesso nem captura que transforme erro em sucesso. A confirmacao de consumo e gerenciada pelo binding RabbitMQ. Os retries, _error e Fault<T> do MassTransit NAO sao herdados. DLX/DLQ devem ser configurados previamente no broker, e tratamento de erro/reentrega deve ser validado com mensagem sintetica em fila isolada antes da troca definitiva. Nao publique mensagens invalidas nas filas existentes para testar.

Nao ha deduplicacao persistente: reentrega pode repetir o log simulado. Um envio real exigira idempotencia duravel por messageId e estrategia de retries/erros. Nao foi introduzido envio de email real.

Rodar o host local ou um container demonstra Functions localmente, nao comprova autoscaling/scale-to-zero serverless em nuvem. Publicacao RabbitMQ no Azure atualmente requer planos Premium/Dedicated, portanto nao deve ser tratada como gratuita.

Referencias: [binding RabbitMQ](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-rabbitmq-trigger) e [envelope MassTransit](https://masstransit.io/documentation/configuration/serialization).

## Compose de desenvolvimento independente

```powershell
docker compose -f docker-compose.dev.yml up -d --build
docker compose -f docker-compose.dev.yml ps -a
docker compose -f docker-compose.dev.yml logs -f notifications-function
```

Inclui Function, Azurite, RabbitMQ e inicializador de topologia, em rede independente. AMQP localhost:5673; Management http://localhost:15673; host Function http://localhost:7072 (sem endpoint de negocio HTTP); Azurite localhost:10010/10011/10012. Portas alternativas evitam conflito com o ambiente integrado e o Azurite avulso. Configuracao e credenciais academicas vem do Compose, nao do local.settings.json. FCG_RABBITMQ_CONNECTION pode apontar para outro broker LOCAL ja provisionado, mas o inicializador padrao provisiona apenas o broker deste Compose.

Para executar somente Azurite por este repositorio:

```powershell
docker compose -f docker-compose.dev.yml up -d azurite
```

Esse Azurite usa conta ficticia fcglocal e portas alternativas; UseDevelopmentStorage=true do arquivo avulso aponta para o Azurite anterior em 10000, nao para este. Para conectar um host avulso ao emulador deste Compose, configure explicitamente os tres endpoints localhost:10010/10011/10012 e a conta/chave ficticias do Compose.

## Fluxo completo integrado

No repositorio irmao fiap-cloud-games-orchestration, execute docker compose up -d --build e docker compose logs -f notifications-function. O ambiente inclui as APIs, bancos, Kong, RabbitMQ, Function e Azurite com provisionamento das filas e Gateway. NotificationsAPI permanece no profile opcional legacy-notifications; nunca executar junto da Function no mesmo broker. Nao e necessario dotnet run ou func start em paralelo.

Dockerfile publica .NET 10 dentro da imagem oficial do host Functions v4. .dockerignore exclui local.settings*, .env*, certificados, Git e testes. Imagem final nao inclui configuracoes privadas. Azurite 3.35.0 usa somente armazenamento local com volume; nada e criado na conta Azure. RabbitMQ/SQL/Mongo continuam locais.

### Validacao integrada Docker em 13/09/2026

Composes validos; imagem da Function construida e confirmada sem local.settings.json ou testes; 22 testes unitarios aprovados. Orquestracao e desenvolvimento independente subiram com Azurite e Function saudaveis. Topologia criada sem NotificationsAPI antiga no broker novo de desenvolvimento; reexecucao idempotente no broker integrado confirmou entidades e bindings sem duplicidade.

Pelo Kong integrado, cadastro 201 e login 200 geraram boas-vindas; jogo temporario 201 e compra 202 percorreram CatalogAPI -> RabbitMQ -> PaymentsAPI -> PaymentProcessedEvent -> CatalogAPI/biblioteca e Function, com confirmacao Succeeded. Biblioteca 200 confirmou aquisicao. Pagamento Rejected sintetico no broker isolado nao enviou confirmacao e terminou com Succeeded.

O teste inicial encontrou erro DNS temporario no Kong enquanto a CatalogAPI iniciava; a repeticao passou e dependencias Kong -> UsersAPI/CatalogAPI foram adicionadas ao Compose para evitar resolucao antes da subida dos upstreams. Os servicos de inicializacao terminaram Exited (0). Cada fila de notificacao integrada ficou com um consumer e zero mensagens pendentes/nao confirmadas.

Usuario/jogo/pedido/item/biblioteca temporarios removidos com verificacoes de identidade e referencias; chave Redis ausente, nenhum documento Mongo criado no teste. Dados existentes preservados. Ambiente independente parado apos validacao, volumes preservados; orquestracao integrada permaneceu rodando. Nenhuma alteracao Kubernetes, publicacao de imagem, commit ou recurso Azure.

## Validacao local realizada em 13/09/2026

- Credenciais do Compose transferidas ao local.settings.json ignorado pelo Git, sem exposicao nos logs; arquivo existente preservado.
- RabbitMQ Docker iniciado e topologia criada pela NotificationsAPI original, posteriormente parada para evitar concorrencia. Nenhuma mudanca no Kubernetes.
- Ambos triggers habilitados no arquivo local (o exemplo versionado permanece desabilitado).
- Build sem erros/avisos e 22 testes aprovados.
- Host reconheceu UserCreatedFunction e PaymentProcessedFunction com rabbitMQTrigger.
- Tres envelopes sinteticos publicados nos exchanges de evento: boas-vindas e compra Approved geraram logs com campos do evento; Rejected nao gerou confirmacao. As tres execucoes terminaram com Succeeded.
- Nenhum usuario, pedido ou compra criado nos bancos; nenhum email real ou recurso Azure. Host temporario encerrado; RabbitMQ/Azurite permanecem locais e NotificationsAPI Docker parada. Inicie o host no seu terminal para experimentar. Reentrega/DLQ e fluxo end-to-end das APIs ainda nao validados nesta etapa.
