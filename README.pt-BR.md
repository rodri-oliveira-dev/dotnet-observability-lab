[English](README.md) | [**Português (Brasil) — idioma atual**](README.pt-BR.md)

# dotnet-observability-lab

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) [![.NET Aspire](https://img.shields.io/badge/.NET-Aspire-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/en-us/dotnet/aspire/) [![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-instrumented-425CC7)](https://opentelemetry.io/)  
[![Compilação, testes e cobertura — main](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/ingestion-integration.yml/badge.svg?branch=main)](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/ingestion-integration.yml) [![CodeQL — main](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/codeql.yml/badge.svg?branch=main)](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/codeql.yml) [![Licença: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![Arquitetura: LikeC4](https://img.shields.io/badge/Architecture-LikeC4-606C38)](docs/architecture/README.md)

Um laboratório educacional e executável de referência com **.NET 10 / Aspire / OpenTelemetry** para estudar processamento assíncrono confiável, idempotência HTTP e rastreamento distribuído entre APIs e workers independentes. A operação de negócio é propositalmente simples — receber um valor decimal — para destacar limites de persistência, entrega pelo menos uma vez, tratamento de duplicações e telemetria, sem ampliar desnecessariamente o domínio ou a estrutura do projeto.

## Visão geral da arquitetura

O [modelo de arquitetura LikeC4 e o índice das visões](docs/architecture/README.md) são a fonte de verdade das visões C4 de Contexto do Sistema, Contêineres, Componentes e do fluxo dinâmico. Este README resume as responsabilidades, sem manter um segundo diagrama. Para visualizar as **visões interativas renderizadas localmente**, execute `npm ci && npm run architecture:dev` na raiz do repositório e abra o endereço exibido pelo LikeC4. A publicação de uma visão renderizada acessível pela internet está prevista na [issue #34](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/issues/34); os arquivos-fonte vinculados aqui não são, por si, diagramas renderizados.

**Caminho nominal dos dados (resumo textual, não um segundo diagrama C4):** `POST /values` → `ingestion_db` (valor + Outbox) → `Ingestion.Outbox.Worker` → RabbitMQ → `Consolidation.Worker` → `consolidation_db` (Inbox + agregado). Uma requisição **posterior e independente** a `GET /consolidated` consulta `consolidation_db`.

As duas APIs **nunca chamam uma à outra**. Um único recurso PostgreSQL local hospeda dois bancos lógicos separados (`ingestion_db` e `consolidation_db`), cada um com seu próprio proprietário e suas credenciais de aplicação sem privilégios de superusuário. O Redis é uma otimização opcional, de melhor esforço, para a idempotência HTTP da `Ingestion.Api`; o PostgreSQL continua sendo a fonte de verdade tanto para a idempotência HTTP quanto para a deduplicação do consumidor. Embora o Aspire disponibilize uma referência ao Redis para o `Consolidation.Worker`, o worker não consulta o cache: ele persiste o identificador da mensagem na Inbox e atualiza o agregado atomicamente em `consolidation_db`. Somente os dois workers se conectam ao RabbitMQ; a entrega é **pelo menos uma vez**, e a Inbox no PostgreSQL garante a **idempotência do efeito de negócio**, não uma entrega exatamente uma vez. O Aspire inicia processos e recursos locais e exibe os dados do OpenTelemetry; ele **não** encaminha o tráfego de negócio. Consulte as [visões C4 de níveis 1–3 e o fluxo dinâmico](docs/architecture/README.md), o [contrato do evento](docs/events/ValueReceived.v1.md) e os [registros de decisões arquiteturais (ADRs)](docs/adr/README.md).

## Pré-requisitos e inicialização

Instale o SDK definido em [global.json](global.json) (.NET 10), uma CLI do Aspire compatível com o AppHost e um mecanismo Docker/OCI em execução. O Node.js 20+ é necessário para as ferramentas de arquitetura e CI, mas **não** para executar o AppHost. Na primeira inicialização de um ambiente local novo, configure os cinco parâmetros secretos persistentes abaixo uma única vez (escolha senhas fortes e não as inclua no repositório):

```bash
aspire secret set Parameters:postgres-password YOUR_ADMIN_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:ingestion-db-password YOUR_INGESTION_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:consolidation-db-password YOUR_CONSOLIDATION_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:rabbitmq-username YOUR_RABBITMQ_USERNAME --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
aspire secret set Parameters:rabbitmq-password YOUR_RABBITMQ_PASSWORD --apphost ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

As mesmas credenciais devem ser mantidas entre reinicializações dos volumes persistentes do PostgreSQL e RabbitMQ. Volumes existentes inicializados com senhas diferentes podem exigir a atualização das senhas e dos papéis de banco ou a recriação **apenas de dados locais descartáveis**. A partir da raiz do repositório, inicie o ambiente completo com **um único comando**:

```bash
aspire run --project ./src/DotNetObservabilityLab.AppHost/DotNetObservabilityLab.AppHost.csproj
```

Abra o endereço do Dashboard exibido pelo Aspire. Aguarde até que `ingestion-api`, `ingestion-outbox-worker`, `consolidation-api`, `consolidation-worker`, `postgres`, `ingestion-db`, `consolidation-db`, `rabbitmq` e `redis` estejam íntegros. Copie o endereço HTTP de cada API na página de recursos do Aspire e configure as variáveis no terminal (as portas são atribuídas durante a execução):

```bash
export INGESTION_API_URL='http://localhost:PORT_FROM_ASPIRE'
export CONSOLIDATION_API_URL='http://localhost:OTHER_PORT_FROM_ASPIRE'
curl -i -X POST "$INGESTION_API_URL/values" \
  -H 'Idempotency-Key: demo-001' -H 'Content-Type: application/json' \
  -d '{"value":10.5}'
curl -i "$CONSOLIDATION_API_URL/consolidated"
```

Uma chave nova retorna **201** com `id` e `value`; a mesma chave e o mesmo valor numérico retornam **200** com o recibo original, enquanto a mesma chave com outro valor retorna **409**. Após o processamento pelos workers, `GET /consolidated` retorna HTTP 200 com `count`, `sum`, `average` e `lastUpdatedAt`. Antes do primeiro evento, os valores numéricos são zero e a data é nula. A API de leitura consulta exclusivamente seu banco de dados e continua respondendo mesmo quando a ingestão está indisponível.

## Explore os comportamentos

O [roteiro prático dos seis cenários](docs/scenarios.md) traz comandos, estados esperados no banco de dados e sinais de **Traces / Structured Logs / Metrics do Aspire** para demonstrar o processamento completo, requisições HTTP duplicadas, entrega AMQP duplicada, indisponibilidade da `Ingestion.Api`, interrupção e recuperação do RabbitMQ e lentidão/erro na consolidação. Inclui injeção de falhas SQL reversível **somente para desenvolvimento**, sem alterações no código de produção. Use a [visão dinâmica do LikeC4](docs/architecture/views.c4) para entender a ordem nominal; o trace distribuído de uma gravação efetiva segue o contexto W3C persistido na Outbox, enquanto um GET posterior inicia outro trace independente.

O [relatório de verificação em ambiente real](docs/runtime-verification.md) distingue as evidências efetivamente obtidas dos procedimentos descritos e dos testes de CI: os seis cenários permanecem **pendentes** até que o Dashboard do Aspire, a repetição de mensagens, as indisponibilidades e as verificações de banco tenham sido inspecionados de fato. O [script opcional de smoke HTTP](scripts/runtime_http_smoke.py) verifica somente as observações HTTP e as variações do modelo de leitura dos dois primeiros cenários em um ambiente local descartável em execução.

## Solução de problemas na primeira execução

Se o Aspire não conseguir iniciar PostgreSQL, RabbitMQ ou Redis, confirme que o mecanismo Docker/OCI está em execução e que os recursos estão íntegros. Se faltar algum parâmetro na inicialização, configure os cinco segredos acima para este AppHost; não grave credenciais no código-fonte. Se a autenticação falhar após reiniciar, os volumes persistentes do PostgreSQL/RabbitMQ podem conter credenciais da inicialização anterior: mantenha os mesmos segredos ou atualize os papéis existentes; recrie **somente dados locais descartáveis**. Se o `curl` não conectar, copie os endereços atuais das APIs na página de recursos do Aspire em vez de presumir portas fixas. Consulte as [orientações de inicialização da arquitetura](docs/architecture/README.md) e o [roteiro de cenários](docs/scenarios.md) para detalhes operacionais.

## Compilação, testes e critérios de qualidade

```bash
dotnet tool restore
npm ci
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
bash scripts/test-with-coverage.sh
```

As cinco suítes de testes .NET verificam o comportamento HTTP, Outbox, Inbox, leitura independente e contratos; os testes de integração PostgreSQL/Testcontainers precisam do Docker. O workflow de PR também verifica os limites de dependências arquiteturais, ADR Guard/índice, validação, formatação e compilação do LikeC4 e uma **cobertura global mínima de 80% das linhas**; esse é um limite configurado, não uma medição atual. Workflows separados de CodeQL e Dependency Review analisam segurança e alterações de dependências. Para reproduzir todos os critérios locais, consulte as [instruções de CI](docs/ci.md). O [índice de documentação](docs/README.md) reúne os detalhes técnicos e operacionais; este README permanece um guia de início rápido, não um tutorial exaustivo de cada ferramenta.
