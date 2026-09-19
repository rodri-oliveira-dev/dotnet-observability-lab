---
name: coverage-analysis
description: Use esta skill para analisar cobertura de testes deste laboratório .NET, identificar gaps relevantes e priorizar testes por risco. Não use para inflar percentual, reduzir qualidade de asserts ou instalar ferramentas sem necessidade concreta.
license: MIT
---

# Objetivo

Usar cobertura como sinal de risco, não como objetivo isolado. A análise deve priorizar comportamento público, complexidade, frequência de mudança e impacto de regressão.

# Quando usar

- O pedido mencionar coverage, cobertura, gaps, hotspots ou risco de refatoração.
- Uma mudança atingir código pouco exercitado por testes.
- For necessário decidir quais cenários testar primeiro.
- A cobertura existir, mas não estiver claro se os testes oferecem confiança suficiente.

# Quando não usar

- Escrever testes novos sem análise de cobertura.
- Corrigir falha funcional de teste sem relação com cobertura.
- Rodar testes apenas para validar build.
- Instalar ferramenta nova quando a baseline existente já coleta cobertura.

# Regras obrigatórias

- Não altere testes apenas para aumentar percentual.
- Não aceite teste sem assert significativo como melhoria real de cobertura.
- Não reduza threshold ou validação para contornar falha sem instrução explícita.
- Não adicione pacote ou ferramenta sem consumidor real.
- Não substitua análise de risco por ranking puramente numérico.
- Considere contrato HTTP/eventos públicos, branches, tratamento de erro, invariantes e caminhos de compatibilidade.

# Fonte de cobertura

O laboratório ainda não possui pipeline oficial de cobertura. Use apenas ferramentas que já estejam configuradas ou obtenha aprovação para introduzir uma nova dependência. Quando existir Coverlet compatível com o runner, use o comando específico do projeto de testes:

```bash
dotnet test ./tests/Ingestion.Api.Tests/Ingestion.Api.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
```

Use relatórios de cobertura somente quando a instrumentação correspondente estiver instalada e configurada. Não declare que o comando acima coleta cobertura sem verificar o collector.

# Processo

1. Leia `AGENTS.md` e identifique o comportamento que deveria estar protegido.
2. Execute ou utilize o relatório de cobertura existente.
3. Relacione gaps de cobertura ao código de produção correspondente.
4. Classifique por risco:
   - alto: contrato HTTP/eventos públicos, regras, branches complexos, validações, erros e compatibilidade;
   - médio: transformação ou coordenação com comportamento observável;
   - baixo: boilerplate, glue code trivial, configuração declarativa ou código gerado.
5. Diferencie ausência de cobertura de cobertura superficial.
6. Sugira testes por comportamento e cenário, não por linha isolada.
7. Se houver mudança de teste, valide a suite completa.

# Saída esperada

- Hotspots priorizados por risco.
- Explicação do comportamento não protegido.
- Separação entre gap aceitável e gap perigoso.
- Cenários de teste recomendados com motivo.
- Validações executadas ou bloqueios encontrados.

# Validação

Após ajustes em testes:

```bash
dotnet restore ./DotNetObservabilityLab.slnx
dotnet build ./DotNetObservabilityLab.slnx --configuration Release --no-restore
dotnet test ./DotNetObservabilityLab.slnx --configuration Release --no-build
dotnet test ./tests/Ingestion.Api.Tests/Ingestion.Api.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
```

# Critério de qualidade

O resultado é bom quando reduz risco real de regressão e melhora a confiança nos comportamentos relevantes, mesmo que o percentual global mude pouco.
