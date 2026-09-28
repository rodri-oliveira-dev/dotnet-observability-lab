[English](case-study.md) | [**Português (Brasil) — idioma atual**](case-study.pt-BR.md)

# Estudo de caso arquitetural: processamento assíncrono confiável com evidências observáveis

Esta página é a versão em português do estudo de caso arquitetural canônico do laboratório. Ela conecta o problema implementado, restrições, atributos de qualidade, decisões, modos de falha, sinais de observabilidade e evidências do repositório sem criar um segundo modelo de arquitetura.

**Os diagramas de arquitetura continuam sendo autoritativos no [LikeC4](architecture/README.md).** Este documento referencia essas visões e explica sua intenção arquitetural; ele não as redesenha manualmente.

## Estratégia de idioma

A documentação técnica detalhada em `docs/` continua canônica em inglês. O [README em inglês](../README.md) aponta para o estudo de caso canônico em inglês, enquanto o [README em Português (Brasil)](../README.pt-BR.md) aponta para esta tradução equivalente. Quando houver divergência entre as versões, a versão em inglês deve ser considerada a referência factual.

## 1. Problema

O laboratório demonstra processamento assíncrono confiável entre serviços .NET executáveis de forma independente, mantendo o domínio de negócio propositalmente pequeno.

Um cliente envia um valor decimal para a `Ingestion.Api`. A aceitação precisa ser durável e idempotente. O valor aceito deve posteriormente alcançar o limite de consolidação sem uma chamada síncrona de API para API. A publicação pode falhar ou ser repetida, o RabbitMQ pode redeliverar mensagens, consumidores podem reiniciar e uma entrega duplicada não pode contabilizar o agregado duas vezes. Operadores precisam conseguir inspecionar o comportamento resultante por meio de traces, logs estruturados, métricas, estado do banco de dados e cenários reproduzíveis.

O caminho de execução implementado é representado pela [visão dinâmica autoritativa do LikeC4](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read) e pelo [contrato versionado do evento `ValueReceived.v1`](events/ValueReceived.v1.md).

## 2. Restrições

O estudo de caso é deliberadamente moldado pelas seguintes restrições explícitas:

- **As APIs HTTP não chamam uma à outra.** Ingestão e consolidação são limites de execução independentes; a API de leitura continua disponível quando a API de escrita está indisponível. Consulte a [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md) e as [visões de Contêineres/Componentes](architecture/README.md).
- **A entrega pelo RabbitMQ é at-least-once.** Mensagens duplicadas ou redeliveradas são esperadas. A correção vem da idempotência durável, não de uma promessa de transporte exactly-once. Consulte a [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md).
- **PostgreSQL é autoritativo para estado durável e deduplicação.** Unicidade e transações protegem a idempotência HTTP, o estado da Outbox, o estado da Inbox e as atualizações do agregado. Consulte a [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md).
- **Redis é best-effort para idempotência HTTP.** Ele é um atalho de desempenho para recibos já persistidos, nunca o limite durável de correção; perda ou falha do Redis faz o fluxo retornar ao PostgreSQL. Consulte a [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md).
- **Aspire compõe e observa o ambiente local; ele não é um roteador de negócio.** O tráfego de negócio passa pelas APIs, bancos de dados, workers e broker. Consulte a [ADR 0001](adr/0001-use-dotnet-aspire-for-local-composition.md).
- **Cada limite de serviço é proprietário de seus dados.** Um único servidor PostgreSQL local hospeda dois bancos lógicos, mas o código de ingestão não pode consultar dados de consolidação e o código de consolidação não pode consultar dados de ingestão. Consulte a [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md).
- **O laboratório é educacional, não uma topologia de referência para produção.** Ele omite deliberadamente algumas preocupações de produção, como retry/backoff limitado do consumidor e uma dead-letter queue, e seu único servidor PostgreSQL local é uma simplificação para desenvolvimento.
- **O domínio é deliberadamente restrito.** Receber e agregar um valor decimal existe para expor mecanismos de confiabilidade e observabilidade, não para modelar um domínio de negócio rico.

## 3. Atributos de qualidade

| Atributo de qualidade | Resposta arquitetural | Evidência principal |
| --- | --- | --- |
| Confiabilidade | Outbox transacional, publisher confirms, mensagens persistentes no RabbitMQ, Inbox durável, acknowledgement após commit | [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [processador da Outbox](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [consumidor](../src/Consolidation.Worker/ConsolidationConsumer.cs) |
| Consistência | Valor + Outbox são commitados atomicamente; Inbox + agregado são commitados atomicamente; unicidade no banco resolve corridas | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [testes de ingestão](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs), [testes de consolidação](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs) |
| Observabilidade | Traces OpenTelemetry, métricas, logs estruturados e propagação W3C persistida através da Outbox | [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md), [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [roteiro de cenários](scenarios.md) |
| Operabilidade | Aspire compõe os recursos, expõe health/telemetria localmente e oferece um ambiente reproduzível | [ADR 0001](adr/0001-use-dotnet-aspire-for-local-composition.md), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs) |
| Recuperabilidade | Linhas pendentes da Outbox sobrevivem a falhas de publicação; redelivery duplicada é segura; leituras duráveis sobrevivem à indisponibilidade da ingestão | [testes da Outbox](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs), [cenários 4 e 5](scenarios.md) |
| Evolutividade | Limites executáveis de forma independente, contrato de integração versionado, bancos lógicos separados, ADRs explícitas e modelo C4 | [ValueReceived.v1](events/ValueReceived.v1.md), [índice de ADRs](adr/README.md), [modelo LikeC4](architecture/README.md) |
| Segurança | Papéis separados sem superusuário no banco, sem acesso cruzado entre limites, segredos externalizados, verificações CodeQL/dependências | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [documentação de CI](ci.md), [workflow CodeQL](../.github/workflows/codeql.yml) |

## 4. Decisões e trade-offs

### 4.1 Mensageria assíncrona em vez de chamadas síncronas entre APIs

**Decisão.** Ingestão e consolidação se comunicam por meio de um evento versionado publicado no RabbitMQ. Nenhuma API HTTP depende do broker ou chama a outra API.

**Por quê.** Uma chamada síncrona tornaria a disponibilidade e a latência da consolidação parte do caminho da requisição de ingestão e propagaria indisponibilidades entre os limites.

**Trade-off.** O sistema aceita consistência eventual e maior complexidade operacional em torno do broker e do tratamento de falhas assíncronas.

**Evidências:** [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [visão dinâmica LikeC4](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read), [visão de componentes da Consolidation.Api](architecture/rendered.md#c4-level-3---consolidationapi).

### 4.2 Outbox transacional para a transição banco → broker

**Decisão.** A `Ingestion.Api` armazena o valor aceito e uma mensagem pendente na Outbox dentro da mesma transação PostgreSQL. Posteriormente, a `Ingestion.Outbox.Worker` reivindica as linhas pendentes e as publica com roteamento obrigatório, entrega persistente e publisher confirms.

**Por quê.** Gravar o estado de negócio e publicar diretamente no broker não pode ser tornado atômico com uma transação local comum de banco de dados.

**Trade-off.** A publicação passa a ser eventualmente consistente e requer polling, agendamento de retry, tratamento de quarentena e consumo seguro contra duplicações.

**Evidências:** [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [handler de ingestão](../src/Ingestion.Api/Values/IngestValueHandler.cs), [processador da Outbox](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [publisher RabbitMQ](../src/Ingestion.Outbox.Worker/RabbitMqOutboxMessagePublisher.cs), [testes da Outbox](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs).

### 4.3 Inbox durável para deduplicação no consumidor

**Decisão.** O `Consolidation.Worker` persiste a identidade do evento em `consolidation_db.inbox_messages` e atualiza o agregado na mesma transação PostgreSQL.

**Por quê.** A redelivery pelo broker é normal em uma entrega at-least-once, inclusive quando o consumidor faz commit com sucesso, mas o acknowledgement se perde.

**Trade-off.** Toda entrega única realiza trabalho durável no banco, e a Inbox precisa de uma política de retenção/operação em um sistema real de longa duração.

**Evidências:** [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [ConsolidationProcessor](../src/Consolidation.Worker/ConsolidationProcessor.cs), [entidade Inbox](../src/Consolidation.Persistence/InboxMessage.cs), [testes de duplicação concorrente](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### 4.4 At-least-once + idempotência em vez de alegações exactly-once

**Decisão.** O contrato de transporte permite explicitamente publicação repetida e redelivery. Os efeitos de negócio são tornados idempotentes por meio de uma identidade durável.

**Por quê.** Um publisher pode perder a certeza após a aceitação pelo broker, e um consumidor pode fazer commit antes que o acknowledgement chegue ao broker. Chamar isso de exactly-once esconderia semânticas reais de falha.

**Trade-off.** Consumidores precisam ser projetados para entrega duplicada, e a observabilidade precisa distinguir trabalho aplicado de trabalho duplicado.

**Evidências:** [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [contrato do evento](events/ValueReceived.v1.md), [cenário de AMQP duplicado](scenarios.md), [testes de telemetria da consolidação](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### 4.5 Correção no PostgreSQL, com Redis apenas como otimização

**Decisão.** PostgreSQL permanece autoritativo para idempotência HTTP e deduplicação do consumidor. Redis armazena em cache somente recibos de ingestão já commitados e pode falhar sem violar a correção.

**Por quê.** Expiração do cache, reinicialização ou indisponibilidade temporária não podem permitir novamente uma operação durável duplicada.

**Trade-off.** Requisições HTTP duplicadas ainda podem consultar o PostgreSQL; portanto, o cache otimiza latência em vez de substituir verificações duráveis.

**Evidências:** [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [IngestValueHandler](../src/Ingestion.Api/Values/IngestValueHandler.cs), [testes de integração para falha do Redis](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs).

### 4.6 Bancos lógicos separados em vez de um schema compartilhado

**Decisão.** O laboratório local usa um único recurso de servidor PostgreSQL, mas dois bancos de dados com propriedade separada: `ingestion_db` e `consolidation_db`, cada um com seu próprio papel de aplicação sem privilégios de superusuário.

**Por quê.** Essa configuração mantém baixo o custo de recursos locais enquanto torna explícitos os limites de propriedade e privilégio.

**Trade-off.** O servidor local continua sendo um domínio de falha compartilhado e não prescreve uma topologia de produção.

**Evidências:** [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs), [provisionamento dos papéis de banco](../src/DotNetObservabilityLab.AppHost/postgres-init/001-boundary-roles.sh).

### 4.7 OpenTelemetry como padrão, Aspire Dashboard como uma visualização local

**Decisão.** Traces, métricas, logs e propagação de contexto usam semântica OpenTelemetry nos quatro processos. O Aspire fornece orquestração local e uma superfície conveniente para telemetria.

**Por quê.** A instrumentação permanece portável em vez de depender de um dashboard local específico.

**Trade-off.** O repositório precisa manter instrumentação e propagação explícitas nas fronteiras assíncronas em vez de depender de um mecanismo de correlação específico de produto.

**Evidências:** [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md), [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [telemetria da ingestão](../src/Ingestion.Api/Values/IngestionTelemetry.cs), [telemetria da Outbox](../src/Ingestion.Outbox.Worker/OutboxTelemetry.cs), [telemetria da consolidação](../src/Consolidation.Worker/ConsolidationTelemetry.cs).

### 4.8 Aspire para composição local, não como arquitetura de produção ou roteamento de negócio

**Decisão.** Aspire é a raiz de composição local dos processos e da infraestrutura e fornece o Dashboard local.

**Por quê.** Ele centraliza inicialização, conexão de recursos, health e telemetria de desenvolvimento sem vazar APIs de orquestração para o comportamento de domínio.

**Trade-off.** Contribuidores precisam de tooling compatível com Aspire, e uma plataforma de implantação em produção ainda requer uma decisão independente.

**Evidências:** [ADR 0001](adr/0001-use-dotnet-aspire-for-local-composition.md), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs), [Contexto do Sistema no LikeC4](architecture/rendered.md#c4-level-1---system-context).

## 5. Fluxo nominal

A representação autoritativa é a [visão dinâmica do LikeC4](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read). Em forma textual:

`POST /values` → `ingestion_db` (valor + Outbox) → `Ingestion.Outbox.Worker` → RabbitMQ → `Consolidation.Worker` → `consolidation_db` (Inbox + agregado) → posteriormente um `GET /consolidated` independente.

Limites principais desse fluxo:

1. `Ingestion.Api` valida a requisição e a chave de idempotência.
2. PostgreSQL comita atomicamente o valor recebido e a linha da Outbox.
3. O worker da Outbox reivindica linhas elegíveis e publica `ValueReceived.v1`.
4. RabbitMQ pode entregar a mensagem uma ou mais vezes.
5. O worker de consolidação valida a identidade no wire e comita atomicamente Inbox + agregado.
6. A mensagem só recebe acknowledgement após processamento durável.
7. Uma requisição de leitura posterior consulta apenas `consolidation_db`; ela não faz parte do trace de escrita.

Para comandos executáveis e telemetria esperada, use o [cenário 1](scenarios.md).

## 6. Modos de falha

| Modo de falha | Comportamento esperado | Evidência verificável |
| --- | --- | --- |
| Requisição HTTP duplicada | Reutilização equivalente retorna o recibo original; reutilização conflitante retorna 409; permanece exatamente um efeito durável de valor/Outbox | [Cenário 2](scenarios.md), [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs), [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md) |
| Mensagem AMQP duplicada/redeliverada | O worker pode recebê-la novamente, mas a unicidade da Inbox impede uma segunda atualização do agregado; telemetria de duplicação é emitida | [Cenário 3](scenarios.md), [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs) |
| Worker da Outbox interrompido | Valores/linhas da Outbox já commitados permanecem duráveis. Após reiniciar, linhas pendentes elegíveis podem ser reivindicadas e publicadas | [OutboxProcessor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [OutboxPublisherTests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs), [Cenário 4](scenarios.md) |
| Falha após aceitação pelo broker, mas antes do commit de `PublishedAt` | A linha da Outbox pode ser publicada novamente; ela mantém a mesma identidade de evento e a Inbox do consumidor torna seguro o efeito de negócio repetido | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [testes da Outbox](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs) |
| RabbitMQ temporariamente indisponível | A ingestão ainda pode aceitar valores de forma durável porque a API não publica diretamente; a publicação da Outbox permanece pendente/em retry e continua quando o broker se recupera | [Cenário 5](scenarios.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md) |
| Redis indisponível/evicto | A idempotência HTTP retorna ao PostgreSQL autoritativo; a correção é preservada embora o caminho rápido seja perdido | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs) |
| Transação de consolidação falha após tentativa de inserir na Inbox | A transação faz rollback tanto da Inbox quanto do agregado; uma entrega transitória pode ser repetida sem um falso marcador de processamento | [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs), [Cenário 6](scenarios.md) |
| API de ingestão indisponível | Leituras consolidadas existentes continuam disponíveis em `consolidation_db`; trabalho da Outbox já commitado pode continuar de forma independente | [Cenário 4](scenarios.md), [ConsolidatedEndpointTests](../tests/Consolidation.Api.Tests/ConsolidatedEndpointTests.cs), [visão LikeC4 da Consolidation.Api](architecture/rendered.md#c4-level-3---consolidationapi) |

O [relatório de verificação em runtime](runtime-verification.md) distingue intencionalmente **procedimentos e evidências de CI** de uma execução real observada manualmente no Aspire. Não trate a descrição de um cenário não executado como prova de que uma demonstração em runtime ocorreu.

## 7. Observabilidade

O desenho de observabilidade segue a [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md).

### Traces

- A instrumentação do ASP.NET Core inicia o trace da requisição de ingestão.
- A transação de ingestão armazena o contexto W3C do trace junto à linha da Outbox.
- O worker da Outbox cria spans de processamento/publicação e injeta o contexto do trace nos headers AMQP.
- O consumidor da consolidação extrai o contexto W3C e dá continuidade ao trace distribuído de escrita.
- Um `GET /consolidated` posterior é um trace independente porque corresponde a outra requisição de leitura.

Código e testes relevantes: [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [OutboxProcessor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [ConsolidationConsumer](../src/Consolidation.Worker/ConsolidationConsumer.cs), [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### Logs estruturados

Os handlers/workers emitem eventos nomeados e estruturados para resultados como requisições de idempotência repetidas/conflitantes, falhas de publicação, mensagens aplicadas e duplicações. Requisições de ingestão recém-aceitas são representadas pela métrica `lab.ingestion.values.accepted` e pelo span `ingestion.accept_value`, e não por um evento nomeado dedicado de log estruturado. O roteiro orienta operadores sobre quais sinais inspecionar em cada cenário em vez de depender de saída livre no console.

### Métricas

O laboratório expõe counters/histograms de baixa cardinalidade para valores aceitos, requisições duplicadas, publicação da Outbox, mensagens consumidas/duplicadas, valores processados e duração do processamento. Os testes validam dimensões limitadas para os principais sinais de consolidação.

Código e testes relevantes: [IngestionTelemetry](../src/Ingestion.Api/Values/IngestionTelemetry.cs), [OutboxTelemetry](../src/Ingestion.Outbox.Worker/OutboxTelemetry.cs), [ConsolidationTelemetry](../src/Consolidation.Worker/ConsolidationTelemetry.cs), [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs).

### Inspeção local

Aspire inicia os recursos locais e apresenta dados do OpenTelemetry em seu Dashboard. Ele é uma superfície de observação/composição, não uma etapa no caminho de negócio. O [roteiro de cenários](scenarios.md) mapeia traces, logs, métricas e estado do banco/broker esperados para cada modo de falha reproduzível.

## 8. Matriz de rastreabilidade

A matriz abaixo liga afirmações arquiteturais à implementação, testes, documentação/diagramas e CI. A CI comprova gates determinísticos do repositório; demonstrações reais de indisponibilidade continuam registradas separadamente em [runtime-verification.md](runtime-verification.md).

| Decisão / afirmação | Implementação | Evidência de testes | Documentação / diagrama | Evidência de CI |
| --- | --- | --- | --- | --- |
| Commit de Valor + Outbox é o limite durável da ingestão | [IngestValueHandler](../src/Ingestion.Api/Values/IngestValueHandler.cs), [persistência da ingestão](../src/Ingestion.Persistence/IngestionDbContext.cs) | [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs) | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [visão dinâmica](architecture/rendered.md#valuereceivedv1---accepted-write-to-independent-consolidated-read) | [workflow de integração](../.github/workflows/ingestion-integration.yml) |
| Publisher da Outbox confirma antes de marcar sucesso | [OutboxProcessor](../src/Ingestion.Outbox.Worker/OutboxProcessor.cs), [RabbitMqOutboxMessagePublisher](../src/Ingestion.Outbox.Worker/RabbitMqOutboxMessagePublisher.cs) | [OutboxPublisherTests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs) | [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [visão de componentes da Outbox](architecture/rendered.md#c4-level-3---ingestionoutboxworker) | [workflow de integração](../.github/workflows/ingestion-integration.yml) |
| Entrega AMQP duplicada produz um único efeito de negócio | [ConsolidationProcessor](../src/Consolidation.Worker/ConsolidationProcessor.cs), [InboxMessage](../src/Consolidation.Persistence/InboxMessage.cs) | [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs) | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [Cenário 3](scenarios.md) | [workflow de integração](../.github/workflows/ingestion-integration.yml) |
| Redis é opcional para a correção | [IngestValueHandler](../src/Ingestion.Api/Values/IngestValueHandler.cs) | [IngestionEndpointTests](../tests/Ingestion.Api.Tests/IngestionEndpointTests.cs) | [ADR 0003](adr/0003-use-postgresql-for-idempotency-correctness-and-redis-as-an-optimization.md), [visão de Contêineres](architecture/rendered.md#c4-level-2---containers) | [workflow de integração](../.github/workflows/ingestion-integration.yml) |
| Limites de leitura e escrita são independentes | [ConsolidatedQuery](../src/Consolidation.Api/Consolidated/ConsolidatedQuery.cs), [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs) | [ConsolidatedEndpointTests](../tests/Consolidation.Api.Tests/ConsolidatedEndpointTests.cs) | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [ADR 0004](adr/0004-use-rabbitmq-for-asynchronous-messaging.md), [visão da Consolidation.Api](architecture/rendered.md#c4-level-3---consolidationapi) | [gates de limites arquiteturais + testes](../.github/workflows/ingestion-integration.yml) |
| Observabilidade usa semântica OpenTelemetry portável | [ServiceDefaults](../src/DotNetObservabilityLab.ServiceDefaults/Extensions.cs), [tipos de telemetria](../src/Consolidation.Worker/ConsolidationTelemetry.cs) | [ConsolidationProcessorTests](../tests/Consolidation.Worker.Tests/ConsolidationProcessorTests.cs), [OutboxPublisherTests](../tests/Ingestion.Outbox.Worker.Tests/OutboxPublisherTests.cs) | [ADR 0005](adr/0005-use-opentelemetry-as-the-observability-standard.md), [roteiro de cenários](scenarios.md) | [workflow de integração](../.github/workflows/ingestion-integration.yml), [CodeQL](../.github/workflows/codeql.yml) |
| Propriedade dos bancos lógicos é aplicada de forma independente | [AppHost](../src/DotNetObservabilityLab.AppHost/AppHost.cs), [provisionamento de papéis](../src/DotNetObservabilityLab.AppHost/postgres-init/001-boundary-roles.sh) | Suítes apoiadas em PostgreSQL sob [tests/](../tests) exercitam cada limite usando seu próprio modelo de persistência | [ADR 0002](adr/0002-use-postgresql-as-the-transactional-source-of-truth.md), [visão de Contêineres](architecture/rendered.md#c4-level-2---containers) | [workflow de integração](../.github/workflows/ingestion-integration.yml), [CodeQL](../.github/workflows/codeql.yml) |

O workflow de PR também verifica consistência do ADR Guard/índice, validação/formatação/build do LikeC4, regras de dependência arquitetural, as cinco suítes de testes .NET e o gate global configurado de cobertura por linhas. Consulte [CI e automação](ci.md).

## 9. Deliberadamente não implementado

Estas omissões e simplificações são intencionais e não devem ser copiadas para produção sem uma decisão separada:

- **Nenhuma alegação de transporte exactly-once.** O desenho adota entrega at-least-once e efeitos idempotentes.
- **Sem retry/backoff limitado do consumidor ou dead-letter queue na v1.** Falhas transitórias do consumidor podem ser recolocadas na fila; sistemas de produção precisam de uma política explícita para mensagens problemáticas e retries.
- **Sem arquitetura de implantação em produção.** Aspire é apenas a camada de composição local/experiência de desenvolvimento.
- **Sem servidor PostgreSQL separado por limite no laboratório local.** Bancos lógicos e papéis separados demonstram propriedade mantendo o ambiente pequeno; o isolamento em produção deve ser decidido de forma independente.
- **Sem autoridade no Redis.** Redis é deliberadamente incapaz de determinar a correção durável.
- **Sem transação distribuída entre PostgreSQL, RabbitMQ e Redis.** Os padrões Outbox/Inbox substituem a atomicidade entre recursos.
- **Sem garantia de ordenação de eventos.** A correção da consolidação não depende de ordenação estrita das mensagens.
- **Sem modelo de domínio rico ou framework de mensageria generalizado.** O laboratório mantém um único fluxo de evento para que os mecanismos de confiabilidade permaneçam inspecionáveis.
- **Sem diagramas de arquitetura duplicados e mantidos manualmente.** O LikeC4 sob `docs/architecture` é a fonte de verdade da arquitetura.
- **Sem alegação de que a descrição dos cenários é evidência de runtime.** Observações reais devem ser registradas em [runtime-verification.md](runtime-verification.md).

## Navegação

- [Arquitetura LikeC4 interativa ao vivo](https://rodri-oliveira-dev.github.io/dotnet-observability-lab/)
- [Visões de arquitetura renderizadas no GitHub](architecture/rendered.md)
- [Decisões arquiteturais](adr/README.md)
- [Cenários de resiliência e observabilidade](scenarios.md)
- [Relatório de verificação em runtime](runtime-verification.md)
- [CI e critérios de qualidade](ci.md)
