# Fushi

Fushi is a Discord bot for running staff or community applications through moderated voting cycles.

Staff configure an intake channel. The bot captures applications posted there, queues them, and publishes them to a review channel when a voting cycle opens. Eligible voters approve, reject, or abstain. When the cycle closes, each submission is decided against the guild’s approval ratio and quorum, and results are announced.

Configuration, voting rights, and schedules are managed with slash commands and interactive Discord Components V2 panels — not free-form text options for every setting.

**Operator guide:** [docs/guide.md](docs/guide.md)  
**Repository:** https://github.com/kyeede/FushiCivs

---

## Contents

- [How it works](#how-it-works)
- [Features](#features)
- [Requirements](#requirements)
- [Quick start](#quick-start)
- [Discord application setup](#discord-application-setup)
- [Commands overview](#commands-overview)
- [Configuration](#configuration)
- [Repository layout](#repository-layout)
- [Architecture](#architecture)
- [Background services](#background-services)
- [Health checks](#health-checks)
- [Production deployment](#production-deployment)
- [Development](#development)
- [Documentation](#documentation)
- [Contributing](#contributing)
- [License](#license)

---

## How it works

```text
Intake channel  →  queued submissions  →  open cycle (review channel)
                                              ↓
                                         votes cast
                                              ↓
                                    cycle closes / finalises
                                              ↓
                         Approved | Rejected | Skipped → results / archive
```

| Concept | Summary |
| --- | --- |
| **Guild** | One Discord server’s config: channels, policy, schedule, voting grants |
| **Cycle** | A timed voting window (scheduled or opened manually) |
| **Submission** | One application captured from intake (or accepted manually) |
| **Vote** | Approve / Reject / Abstain from a granted user or role holder |
| **Skipped** | Outcome when deciding votes fall below quorum — not a rejection |

Default schedule is Monday, Wednesday, and Saturday, 10:00–22:00 in `Europe/Berlin`. Days, times, and time zone are configurable per guild. Zones use IANA identifiers so daylight saving is handled correctly.

Voting is **deny by default**. Someone may vote only after an explicit user or role grant. Discord’s Manage Server / Administrator permissions control bot configuration, not voting rights.

---

## Features

- **Channel routing** — Intake (text, announcement, thread, or forum), review, optional results / archive / log
- **Voting policy** — Approval threshold, quorum, and toggles for abstentions, self-votes, and vote changes
- **Component-driven config** — `/config` opens panels with channel menus, selects, and switches (no typed channel IDs)
- **Voters** — Bulk grant / revoke via mentionable selects; optional notes on grants
- **Cycles** — Scheduler-driven open / close / finalise; manual override via `/cycle`
- **Short codes** — Six-character Crockford Base32 codes (e.g. `7K4M2P`) with autocomplete; `I`/`L` → `1`, `O` → `0` on input
- **Audit trail** — Configuration, grants, cycle transitions, and votes recorded with actor and reason
- **Idempotent workers** — Safe to restart mid-transition; passes converge on the correct state

---

## Requirements

| Dependency | Purpose |
| --- | --- |
| [.NET SDK](https://dotnet.microsoft.com/download) | Version pinned in [`global.json`](global.json) (currently .NET 11 preview) |
| [Docker](https://docs.docker.com/get-docker/) + Compose V2 | Local PostgreSQL; production image / stack |
| Discord application | Bot token and correct intents / OAuth scopes |
| POSIX shell | Scripts under `build/` (Git Bash or WSL on Windows) |

Optional:

- `dotnet-ef` (prerelease) — scaffolding or applying migrations by hand  
- Redis — second tier for `HybridCache` when running more than one instance  

**Globalization:** keep `InvariantGlobalization` false. Scheduling resolves zones such as `Europe/Berlin` and needs ICU and tzdata. The production Dockerfile installs `tzdata` on the Ubuntu-based ASP.NET image for that reason.

---

## Quick start

```bash
git clone https://github.com/kyeede/FushiCivs.git
cd FushiCivs

cp .env.example .env
# Required: Discord__Token
# Recommended while developing: Discord__DevelopmentGuildId=<your guild snowflake>

docker compose up -d --wait
build/migrate.sh update

set -a && . ./.env && set +a
dotnet run --project src/Fushi.Host
```

`docker compose up -d --wait` blocks until PostgreSQL is healthy so migrations do not race a cold volume.

Optional profiles:

```bash
docker compose --profile cache up -d   # Redis on loopback
docker compose --profile tools up -d   # Adminer at http://localhost:8080
```

After the host connects, configure the server in Discord (`/config channels`, policy, schedule, voters). Step-by-step: [docs/guide.md](docs/guide.md#first-time-setup).

---

## Discord application setup

In the [Discord Developer Portal](https://discord.com/developers/applications):

1. Create an application and add a bot; copy the token into `Discord__Token`.
2. Enable privileged gateway intents, including **Message Content** (required to capture intake posts).
3. Generate an invite with scopes `bot` and `applications.commands`, plus permissions to read the intake channel and post in review / results / archive / log as needed.

Full intent and permission notes: [docs/configuration.md](docs/configuration.md#getting-a-bot-token).

| Variable | Role |
| --- | --- |
| `Discord__Token` | Bot authentication (required) |
| `Discord__DevelopmentGuildId` | Register slash commands to one guild instantly; leave empty in production for global registration |

Global command propagation can take up to about an hour the first time.

---

## Commands overview

| Group | Purpose |
| --- | --- |
| `/config` | Channels, voting policy, schedule, enable / disable |
| `/voter` | Grant, revoke, and list voting rights |
| `/cycle` | Status, list, open / close / finalise / cancel |
| `/submission` | Inspect, list, accept, withdraw |
| `/vote` | Cast or retract a vote (also available from review message buttons) |

Most `/config` and `/voter grant|revoke` flows open interactive panels rather than taking many slash options. Exact options and permission gates: [docs/interactions.md](docs/interactions.md). Usage narrative: [docs/guide.md](docs/guide.md#command-reference).

---

## Configuration

Settings load in this order (later wins):

`appsettings.json` → `appsettings.{Environment}.json` → environment variables / `.env`

Nested keys use `__` in the environment (e.g. `Discord__Token`, `ConnectionStrings__Database`, `Scheduler__TickSeconds`).

| Area | Examples |
| --- | --- |
| Discord | `Discord__Token`, `Discord__DevelopmentGuildId` |
| Database | `ConnectionStrings__Database`, `Database__ApplyMigrationsOnStartup` |
| Scheduler | `Scheduler__TickSeconds`, `IntakeSeconds`, `RegistrationSeconds` |
| Telemetry | `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_SERVICE_NAME` |

Templates:

- [`.env.example`](.env.example) — local development  
- [`.env.production.example`](.env.production.example) — VPS / Compose production  

Never commit a filled `.env`. It is gitignored.

Complete key list and defaults: [docs/configuration.md](docs/configuration.md).

---

## Repository layout

```text
Fushi.slnx
Directory.Build.props / .targets     Shared MSBuild settings
Directory.Packages.props             Central package versions
global.json                          SDK pin and test runner
.env.example                         Local env template

src/
  Fushi.Core/                        Entities, Result, short codes, paging
  Fushi.Application/                 Commands, queries, handlers, validators, pipeline
  Fushi.Infrastructure/              EF Core, repositories, audit interceptor
  Fushi.Interactions/                Slash modules, Components V2, Discord publishing
  Fushi.Gateway/                     Socket client, readiness, Discord adapters
  Fushi.Host/                        Composition root, health, hosted schedulers

tests/
  Fushi.Core.Tests/
  Fushi.Application.Tests/
  Fushi.Infrastructure.Tests/        Testcontainers against real PostgreSQL (and Redis when used)

build/
  Dockerfile                         Multi-stage Host image
  build.sh                           Restore, format, build, test
  migrate.sh                         EF Core migration helper

docker-compose.yml                   Dev: PostgreSQL (+ Redis / Adminer profiles)
docker-compose.prod.yml              Prod: PostgreSQL + bot
docs/                                Guides and references
```

Project references point **inward only**:

`Core` ← `Application` ← `Infrastructure` / `Interactions` / `Gateway` ← `Host`

Build output lands under `artifacts/` (`UseArtifactsOutput`). The debugger targets `artifacts/bin/Fushi.Host/debug/Fushi.Host.dll`.

---

## Architecture

Fushi uses a layered layout with CQRS at the application boundary.

| Layer | Responsibility |
| --- | --- |
| **Core** | Domain model and shared primitives; no infrastructure packages |
| **Application** | Use cases as commands/queries and handlers; declares Discord/persistence ports |
| **Infrastructure** | EF Core mappings, repositories, interceptors |
| **Interactions** | Discord.Net modules, component IDs, message layouts |
| **Gateway** | Connection lifecycle and Discord adapters for those ports |
| **Host** | Wiring, HTTP health endpoints, background services |

Requests go through a pipeline: **logging → validation → unit of work** (commands only). Handlers do not call `SaveChanges` themselves; successful commands are committed by the pipeline. Expected failures return `Result` / `Result<T>` with an `Error`. Exceptions are reserved for unexpected faults.

Dispatch is implemented in-process (not MediatR). Handlers and validators are registered by assembly scan at startup.

More detail: [docs/architecture.md](docs/architecture.md).  
Domain rules and voting math: [docs/domain.md](docs/domain.md).

---

## Background services

| Service | Role |
| --- | --- |
| **GuildRegistrar** | Ensures every guild the bot is in has a configuration row |
| **CycleScheduler** | Opens, closes, and finalises cycles from each guild’s schedule |
| **IntakeSweeper** | Polls intake history and captures new applications |

All three wait for the Discord gateway, then run on a timer. They are convergent and idempotent: a missed tick or restart is corrected on the next pass rather than relying on a single event delivery.

Intervals are under the `Scheduler` configuration section (see `.env.example`).

---

## Health checks

The host exposes:

| Endpoint | Meaning |
| --- | --- |
| `/health/live` | Process is up |
| `/health/ready` | Gateway connected and database reachable |

In production Compose, these bind to loopback inside the container (`ASPNETCORE_URLS`). Use them from an orchestrator or `docker compose exec` as needed. See [docs/operations.md](docs/operations.md#health).

---

## Production deployment

Preferred path: build the image on a development machine, ship the image plus Compose files — **source tree stays off the server**.

```bash
docker build -f build/Dockerfile -t fushi:1.0 .
docker save fushi:1.0 | gzip > fushi-1.0.tar.gz

# On the server: docker load, copy docker-compose.prod.yml + .env, then:
docker compose -f docker-compose.prod.yml up -d --wait
```

The production stack sets `restart: unless-stopped`, keeps Postgres on loopback, and can apply migrations on startup for a single instance (`Database__ApplyMigrationsOnStartup`).

Full Debian / Docker runbook, updates, logs, and backups: [docs/operations.md](docs/operations.md#deploying-to-a-vps-debian--docker).

---

## Development

```bash
build/build.sh --format      # restore, verify formatting, Release build, test
build/build.sh --coverage    # same, with coverage under artifacts/

build/migrate.sh add <Name>  # scaffold a migration
build/migrate.sh update      # apply pending migrations
build/migrate.sh list
```

Useful local practices:

- Set `Discord__DevelopmentGuildId` so slash commands appear immediately  
- Prefer `dotnet watch --project src/Fushi.Host run` for iteration; avoid rapid reconnect loops (Discord identify rate limits)  
- Do not run a local Host against the same bot token as a production instance  

Analyzer warnings are advisory locally and treated as errors in CI (`ContinuousIntegrationBuild`). Policy: [CONTRIBUTING.md](CONTRIBUTING.md).

---

## Documentation

| Document | Contents |
| --- | --- |
| [docs/guide.md](docs/guide.md) | Using the bot: setup, commands, panels, troubleshooting |
| [docs/interactions.md](docs/interactions.md) | Slash commands, components, permissions |
| [docs/configuration.md](docs/configuration.md) | Config keys, defaults, Discord intents and scopes |
| [docs/operations.md](docs/operations.md) | Local run, migrations, deploy, monitoring, backups |
| [docs/architecture.md](docs/architecture.md) | Layers, dependency rule, request pipeline |
| [docs/domain.md](docs/domain.md) | Entities, lifecycles, voting arithmetic, short codes |

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branching, commit style, formatting, and tests.

Notable changes are recorded in [CHANGELOG.md](CHANGELOG.md) ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)).

---

## License

MIT. See [LICENSE](LICENSE).
