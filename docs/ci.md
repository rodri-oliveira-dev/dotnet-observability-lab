# CI e automações do laboratório

Este laboratório utiliza os workflows existentes e seleciona somente os controles
compatíveis com APIs/workers .NET 10, PostgreSQL, Redis e ferramentas de arquitetura.
A seleção foi adaptada de
[ReliableWebhooks/.github](https://github.com/rodri-oliveira-dev/ReliableWebhooks/tree/ea474da1c17e19b15b63c3202c8078e9e90cc1e3/.github).

| Arquivo | Execução | Objetivo |
| --- | --- | --- |
| `.github/workflows/ingestion-integration.yml` | PRs relevantes, pushes em `main`, execução manual | Restore e build da solution; testes HTTP/EF Core com PostgreSQL/Testcontainers; ADR Guard e LikeC4. Verifica também pins imutáveis dos `uses:` nos workflows. |
| `.github/workflows/codeql.yml` | PRs, pushes em `main` e agenda semanal | Análise de segurança C# com CodeQL (`security-extended`) usando build manual da solution, sem publicar artefatos nem exigir credenciais extras. |
| `.github/workflows/dependency-review.yml` | PRs para `main` | Verifica o delta de dependências e falha caso a revisão encontre vulnerabilidades de severidade `high` ou superior. |
| `.github/dependabot.yml` | Agendamento semanal | Propõe PRs de atualização de NuGet/CPM, SDK em `global.json`, npm/LikeC4 e GitHub Actions; agrupa versões minor e patch. |

## Segurança e condições de execução

- Actions de terceiros ficam fixadas por SHA completo. Os workflows usam
  `contents: read` para limitar permissões de escrita remota e os checkouts não
  persistem credenciais (`persist-credentials: false`). O job do CodeQL tem
  permissão adicional `security-events: write` somente para enviar resultados.
- O Dependency Review requer que o grafo de dependências esteja disponível no
  repositório. A análise CodeQL requer a funcionalidade de code scanning do GitHub.
  Verifique os resultados reais de cada workflow após a criação do PR.
- A revisão de dependências inspeciona o **delta do PR**, não substitui auditoria
  periódica de todo o grafo; o restore .NET mantém as verificações `NuGetAuditMode=all`
  já configuradas. O comando `npm ci` valida o lockfile do LikeC4.
- As regras de proteção de branch, alertas de segurança e configurações de conta
  não são criadas pela cópia dos arquivos YAML.

## Não importados

O `release.yml` de ReliableWebhooks publica pacotes NuGet e manipula tags,
artefatos e credenciais; o laboratório ainda não possui fluxo de publicação
ou entrega de seus serviços. Importá-lo criaria automações sem consumidor.
O `sonar.yml` depende de configuração e token para um projeto SonarQube Cloud,
além de um scanner específico para Sonar que não faz parte da baseline
deste laboratório. Adicioná-lo como check opcional que sempre é ignorado
produziria um status pouco informativo. Essas integrações devem ser propostas
separadamente quando houver estratégia de release ou projeto Sonar provisionado.

## Issue #11: quality and coverage gates

The existing ingestion-integration workflow runs on every PR to main, every push to main and manual dispatch; it now has no PR path exclusions. It restores .NET/Node tools, checks transitive dependency boundaries, runs ADR Guard, regenerates the ADR index and fails if docs/adr/README.md differs from the committed index. It validates, formats and builds LikeC4 before restoring and building the solution and running all five test projects against PostgreSQL where required.

To reproduce the new gates locally after restoring tools and building the solution in Release mode:

```bash
python3 scripts/check-architecture.py
dotnet tool run adr-guard index docs/adr
git ls-files --error-unmatch -- docs/adr/README.md >/dev/null
git diff --exit-code -- docs/adr/README.md
bash scripts/test-with-coverage.sh
```

The coverage script uses coverlet.msbuild 10.0.1 with explicit MSBuild properties, producing Cobertura/OpenCover under TestResults/coverage. Its Python gate counts each production source line once across test suites (covered if any suite executes it), rejects missing/empty reports **and missing non-excluded production source files**, and fails below **80% global line coverage**. The only source-file exclusions are generated code, migrations, design-time DbContext factories, `Program.cs` and the Aspire `AppHost.cs` composition root (bootstrap), plus the declaration-only `IOutboxMessagePublisher.cs` interface (no executable lines); application behavior and telemetry remain included. The architecture gate checks transitive project boundaries, prohibits transport/composition packages or project dependencies in pure domain/data projects, and rejects forbidden C# references in their source and application business handlers (including aliases and fully qualified names). Local PostgreSQL integration tests require Docker. CodeQL, Dependency Review and Dependabot remain independently configured; no other security or release automation was imported.
