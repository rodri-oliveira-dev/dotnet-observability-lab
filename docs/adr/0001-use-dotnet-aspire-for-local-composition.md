# Use .NET Aspire for local composition

## Status

Accepted

## Context

The lab consists of multiple independently executable .NET processes and will progressively add PostgreSQL, Redis, and RabbitMQ. The local development experience needs one explicit place to compose these resources, wire service dependencies, expose health information, and inspect the running topology.

The project also needs a local observability surface that makes it easy to inspect distributed behavior while the later OpenTelemetry scenarios are developed.

Using ad hoc startup scripts or a hand-maintained local composition file for every .NET process would duplicate configuration and make resource relationships harder to understand.

At the same time, the observability design must remain portable. The project must not couple application instrumentation or telemetry semantics to a specific local dashboard.

## Decision

Use **.NET Aspire** as the local application composition and developer-experience layer.

The Aspire AppHost is the local composition root for the executable applications and, in later issues, their infrastructure dependencies. ServiceDefaults provides shared development-time defaults such as health checks, service discovery, resilience defaults, and the baseline OpenTelemetry wiring.

Use the **Aspire Dashboard** as the local surface for inspecting resources and telemetry during development.

Aspire is **not** the observability standard of this repository. **OpenTelemetry remains the instrumentation, context-propagation, and telemetry standard**, and its adoption is documented separately when the end-to-end observability work is introduced.

Aspire must not become part of the business message path, and domain/application code must not depend on Aspire APIs.

This decision is about local composition and developer experience. It does not prescribe the production deployment platform.

## Consequences

Positive consequences:

- the four .NET processes have one explicit local composition root;
- resource wiring and health visibility are centralized in the AppHost;
- infrastructure resources can be introduced incrementally without scattering local startup configuration;
- the Aspire Dashboard provides an immediate local view of resources and, later, OpenTelemetry data;
- application telemetry remains portable because OpenTelemetry is kept as the standard beneath the dashboard.

Trade-offs and constraints:

- contributors need compatible .NET and Aspire tooling for the full local experience;
- the AppHost and Aspire-specific configuration remain development/composition concerns that must not leak into business logic;
- a future production deployment target still requires its own explicit decision and configuration;
- architecture documentation must distinguish Aspire orchestration from the functional flow between application services.
