# Documentation

This directory is the entry point for detailed project documentation. Start with the [English README](../README.md) or the [README em Português (Brasil)](../README.pt-BR.md) for equivalent first-run instructions and a language switcher.

## Architecture

- [GitHub-rendered LikeC4 gallery — Context, Containers, four Component views and dynamic flow](architecture/rendered.md)
- [Architecture overview, view navigation and the authoritative C4 model](architecture/README.md)
- [Rendered PNGs and interactive static site — download from the architecture workflow](https://github.com/rodri-oliveira-dev/dotnet-observability-lab/actions/workflows/architecture-preview.yml)
- [Architecture Decision Records](adr/README.md)
- [Versioned integration event: ValueReceived.v1](events/ValueReceived.v1.md)
- [Six reproducible resilience and observability scenarios](scenarios.md)

The repository intentionally separates **current architecture documentation** from **historical architectural decisions**:

- LikeC4 describes the architecture that should match the current implementation.
- ADRs explain why important architectural decisions were made.

The root [README](../README.md) remains the quick-start entry point; detailed commands and expected dashboard signals live in the scenario runbook.
