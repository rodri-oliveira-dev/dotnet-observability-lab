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

- Actions de terceiros ficam fixadas por SHA completo, e checkouts são read-only
  (`persist-credentials: false`). O job do CodeQL tem permissão adicional
  `security-events: write` somente para enviar resultados.
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
além de um scanner e coleta de cobertura que não fazem parte da baseline
deste laboratório. Adicioná-lo como check opcional que sempre é ignorado
produziria um status pouco informativo. Essas integrações devem ser propostas
separadamente quando houver estratégia de release ou projeto Sonar provisionado.
