# Workspace setup

Open [`dotnet-observability-lab.code-workspace`](../dotnet-observability-lab.code-workspace)
in Visual Studio Code, then run **Terminal → Run Task → Workspace: Bootstrap** once
after cloning or whenever either dependency lock file changes.

The bootstrap task runs these commands in sequence and stops on the first failure:

```text
dotnet tool restore
dotnet restore ./DotNetObservabilityLab.slnx
npm ci
```

This installs only repository-managed dependencies. It restores `dotnet-ef` and
ADR Guard from `.config/dotnet-tools.json`, NuGet packages from the solution and
the pinned LikeC4 package from `package-lock.json`. It does not install global or
operating-system software, modify `PATH`, create Aspire secrets or start containers.

The workspace also provides tasks for the Release build, tests, ADR validation,
LikeC4 validation and the Aspire AppHost. The AppHost task still requires the five
local secrets documented in the root README.

## External prerequisites

Install these tools before running the bootstrap. Versions should be verified in a
new terminal so Visual Studio Code inherits the updated `PATH`.

| Tool | Requirement | Verify | Why it is external |
| --- | --- | --- | --- |
| Visual Studio Code | Current supported release | `code --version` | Opens the `.code-workspace`; optional if another editor is used. |
| Git | Current supported release | `git --version` | Clone, branch and evidence commit identity. |
| .NET SDK | Version selected by `global.json` | `dotnet --version` | The workspace cannot install a machine SDK safely. |
| Aspire CLI | Compatible with the AppHost; currently validated with 13.5.x | `aspire --version` | Global cross-platform CLI used to run and inspect the lab. |
| Docker-compatible engine | Docker API with Linux containers; Rancher Desktop must use `moby` | `docker version` | Runs PostgreSQL, RabbitMQ, Redis and Testcontainers. |
| Node.js and npm | Node.js 20 or newer | `node --version` and `npm --version` | `npm ci` installs the pinned LikeC4 dependency. |
| Python | 3.10 or newer | `python --version` or `python3 --version` | Runs the optional runtime evidence collectors. |
| PostgreSQL client | `psql` available on `PATH` | `psql --version` | Queries the two application databases during runtime verification. |
| Bash | Git Bash, WSL or native Bash | `bash --version` | Required by the coverage and architecture-generation scripts. |

Official installation sources:

- [.NET SDK](https://learn.microsoft.com/dotnet/core/install/)
- [Aspire CLI](https://learn.microsoft.com/dotnet/aspire/cli/overview)
- [Docker Engine](https://docs.docker.com/engine/install/) or
  [Rancher Desktop](https://docs.rancherdesktop.io/getting-started/installation/)
- [Node.js](https://nodejs.org/en/download)
- [Python](https://www.python.org/downloads/)
- [PostgreSQL client](https://www.postgresql.org/download/)
- [Git](https://git-scm.com/downloads)
- [Visual Studio Code](https://code.visualstudio.com/download)

## Windows and Rancher Desktop

For native Windows tests with Rancher Desktop, keep the Resource Reaper enabled
and configure Testcontainers in `%USERPROFILE%\.testcontainers.properties` if
Docker endpoint auto-discovery fails:

```properties
docker.host=npipe://./pipe/docker_engine
```

The runtime evidence collector invokes `psql` by name. If the PostgreSQL installer
did not update `PATH`, add its `bin` directory to the current private shell before
running evidence commands. Do not put passwords, connection strings or Aspire
secret values in the workspace file.

## Recommended extensions

The workspace recommends C# Dev Kit and Microsoft Container Tools. Visual
Studio Code asks before installing recommendations; neither extension replaces the
external SDK, Aspire CLI or container runtime requirements above.
