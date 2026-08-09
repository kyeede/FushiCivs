# Fushi

A Discord bot that runs community applications through moderated voting cycles.

Staff nominate an intake channel; Fushi captures applications posted there, queues
them, and publishes them to a review channel when the next voting cycle opens.
People who have been explicitly granted voting rights approve, reject, or abstain.
When the cycle closes, each submission is decided against the guild's configured
approval ratio and quorum, and the results are announced.

The behaviour that makes it worth building rather than doing by hand is in the
edges: a submission that nobody voted on is **Skipped**, not rejected; abstentions
count as participation but affect neither the ratio nor the quorum; and cycles run
on a wall-clock schedule in a real time zone, so `Europe/Berlin` means 10:00 local
in January and 10:00 local in July rather than a fixed offset that drifts by an
hour twice a year.

## Features

- **Per-guild configuration.** Intake, review, results, archive, and log channels;
  approval ratio; quorum; abstention, self-vote, and vote-change policy; the
  recurring schedule and its time zone. All changed through `/config`, all with
  working defaults from the moment the bot joins.
- **Deny-by-default voting rights.** Voting is opened by explicit grant to a user
  or a role, never inherited from a Discord permission. Grants are additive and
  there is no deny rule, so the effective answer is always predictable. A
  configuration mistake locks people out — which someone reports — rather than
  quietly letting the wrong people decide.
- **Recurring cycles with correct time handling.** Default Monday, Wednesday, and
  Saturday, 10:00–22:00 `Europe/Berlin`. Zones are stored as IANA identifiers, and
  both daylight saving edge cases are handled deliberately: a local time that never
  happened moves forward to the first that did, and one that happened twice takes
  the earlier instant for opening and the later for closing, so a window is never
  shortened by a transition.
- **Approved, Rejected, and Skipped as three distinct outcomes.** Quorum and ratio
  are independent gates. Missing quorum means the panel did not decide, which is
  information about the panel's availability rather than about the application, and
  it is not recorded as a rejection.
- **Six-character short codes.** Every entity a user can address carries a
  Crockford Base32 code — `7K4M2P` — alongside its internal GUID. `I` and `L` fold
  to `1` and `O` folds to `0` on input, so a misread still resolves. Codes are
  unique per guild, enforced by a database index with retry on collision.
- **Immutable audit trail.** Every configuration change, grant, cycle transition,
  and vote is recorded with its actor, its reason, and the subject's code, so
  "why is this rejected when I approved it" has an answer.
- **Cycles the scheduler drives, and commands that can override it.** `/cycle
  open`, `close`, `finalise`, and `cancel` exist for the cases a schedule does not
  cover. Transitions are idempotent, so a restart mid-transition completes the job
  rather than failing.
- **Autocomplete everywhere a code is asked for**, matching on code prefix or title
  substring and filtered to states where the command can actually succeed.

## Toolchain

Versions are exact. The `Microsoft.Extensions.*` and EF Core builds are pinned to
the SDK band in `global.json` because preview assemblies from different bands do
not interoperate — they move together or not at all.

| Component | Version |
| --- | --- |
| .NET SDK | `11.0.100-preview.6.26359.118` |
| Target framework | `net11.0`, `LangVersion=preview` for C# 15 |
| Discord.Net | `3.20.1` |
| EF Core | `11.0.0-preview.6.26359.118` |
| Npgsql provider | `11.0.0-preview.6` |
| FluentValidation | `12.1.1` |
| Test framework | xUnit v3 via `xunit.v3.mtp-v2` on Microsoft.Testing.Platform |
| Assertions | Shouldly |
| Mocks | NSubstitute |
| Integration tests | Testcontainers (PostgreSQL, Redis) |
| Database | PostgreSQL 17 |
| Cache | `HybridCache`, in-memory L1 with optional Redis L2 |

### Why .NET 11 preview

C# 15 is only available there, and the language features this codebase relies on
are not backportable. Extension members are the clearest example: they are what
let the `Result` combinators in `Fushi.Core/Extensions` read as members of the
type they extend rather than as static helpers taking it as a first argument. The
`field` keyword is the other one in active use, in the value objects whose getters
normalise a default that a struct created without its constructor would otherwise
report as zero.

`LangVersion=preview` also makes union types and closed hierarchies available.
Neither is used yet — `closed enum` in particular is not implemented in
preview.6 — but the error and outcome hierarchies are the obvious candidates once
they land, and the setting means adopting them will not need a toolchain change.
EF Core 11 matches the SDK band, so the data layer sits on the same preview line
as the runtime rather than straddling two.

There is a second, less voluntary reason. There is no official MySQL provider for
EF Core 10 or 11, and Pomelo — the provider most MySQL-on-EF-Core projects use —
tops out at EF Core 9. Staying on MySQL would have meant an unofficial community
fork or freezing EF two majors behind the SDK. Npgsql ships a provider for every EF
Core major in lockstep, including previews, so PostgreSQL was the choice that let
the rest of the stack stay current. The reasoning is recorded in
[docs/architecture.md](docs/architecture.md#why-postgresql-and-not-mysql) and in a
comment in `Directory.Packages.props`.

### Prerequisites

- The .NET SDK version named in `global.json`. `rollForward` is `latestFeature`
  with `allowPrerelease`, so a later preview of the same band works.
- Docker, for the PostgreSQL container and for the Testcontainers-based
  integration tests.
- `dotnet-ef`, if you intend to touch the schema:
  `dotnet tool install --global dotnet-ef --prerelease`.
- A POSIX shell for the scripts in `build/`. On Windows, Git Bash or WSL.

`InvariantGlobalization` is `false` and must stay false: resolving `Europe/Berlin`
needs full ICU data. Any container image must carry ICU and tzdata.

## Quick start

```bash
git clone <repository-url> fushi
cd fushi

cp .env.example .env
# Fill in Discord__Token, and Discord__DevelopmentGuildId for instant
# slash-command registration in your test server.

docker compose up -d --wait          # PostgreSQL, healthchecked
build/migrate.sh update              # apply the schema

set -a && . ./.env && set +a         # export configuration into the shell
dotnet run --project src/Fushi.Host
```

`--wait` blocks until the database health check passes, so the migration does not
race a container that has started but is not yet accepting connections.

Optional tiers, both off by default:

```bash
docker compose --profile cache up -d   # Redis, the HybridCache L2 tier
docker compose --profile tools up -d   # Adminer on http://localhost:8080
```

Build and test the way CI does:

```bash
build/build.sh --format      # restore, verify formatting, build Release, test
build/build.sh --coverage    # the same, with coverage into artifacts/
```

Getting a bot token, and the gateway intents and OAuth scopes it needs — including
the privileged `MessageContent` intent, without which no submission can be
captured — are covered in
[docs/configuration.md](docs/configuration.md#getting-a-bot-token).

## Project layout

Six projects under `src/`, dependencies pointing inward only. `Fushi.Core`
references nothing; `Fushi.Application` references only `Fushi.Core`; everything
else references `Fushi.Application`. A violation is a compile error rather than a
review comment, because the reference simply does not exist.

```
Fushi.slnx                       Solution (slnx, not sln)
Directory.Build.props            Repository-wide MSBuild settings
Directory.Build.targets          Convention-driven per-project configuration
Directory.Packages.props         Central package management: every version, once
global.json                      SDK band, and the MTP test runner selection
.env.example                     Tracked template for .env

src/Fushi.Core/                  Abstractions, Entities (Audits, Cycles, Guilds,
                                 Submissions), Errors, Exceptions, Extensions,
                                 Identifiers, Results, Utilities
                                 Zero package references, by design.
src/Fushi.Application/           Abstractions (Discord, Messaging, Persistence),
                                 Behaviors, Dispatching, Errors, Features, Logging
src/Fushi.Infrastructure/        Persistence (Configurations, Converters,
                                 Repositories), Caching
src/Fushi.Interactions/          Discord interaction modules and components
src/Fushi.Gateway/               Discord gateway lifecycle
src/Fushi.Host/                  Composition root, hosted services, scheduler

tests/Fushi.Core.Tests/          Domain rules. No infrastructure, runs in ms.
tests/Fushi.Application.Tests/   Handlers, validators, pipeline ordering
tests/Fushi.Infrastructure.Tests/ Mappings, migrations, queries, against real
                                 engines via Testcontainers

build/Dockerfile                 Multi-stage image for Fushi.Host
build/build.sh                   Restore, build, test, optional coverage
build/migrate.sh                 EF Core migration wrapper
docker-compose.yml               PostgreSQL, plus Redis and Adminer behind profiles
docs/                            See below
artifacts/                       All build output (UseArtifactsOutput)
```

Build output goes to `artifacts/` rather than per-project `bin/` and `obj/`, which
is why the debugger targets `artifacts/bin/Fushi.Host/debug/Fushi.Host.dll`.

## CQRS without MediatR

Commands, queries, and handlers — no classes named `*Service`. A service class is
where unrelated operations accumulate until nobody can say what depends on what; a
handler has one entry point and a constructor listing exactly what that operation
needs.

The dispatch machinery is written in-house rather than taken from MediatR, which
moved to a commercial licence. The interesting part of the replacement is a small
one. `IDispatcher.SendAsync` receives an `IRequest<TResponse>` whose concrete type
is known only at run time, while `IRequestHandler<TRequest, TResponse>` needs both
types. Bridging that gap requires one generic instantiation per request type, so
`Dispatcher` does it once: the first request of a given type constructs an executor
closed over both types and caches it, after which dispatching is a dictionary
lookup and a virtual call. Reflection is paid once per request type, not once per
request, and none of it is visible from a handler.

Handlers and validators are discovered by scanning the application assembly at
startup rather than being registered by hand — twenty handlers listed individually
is twenty chances to add an operation and forget it, and that failure would only
appear when someone ran the command.

### The pipeline

Three behaviours wrap every request, outermost first:

| Order | Behaviour | What it does |
| --- | --- | --- |
| 1 (outermost) | Logging | Records the request, its outcome, and its duration through source-generated `LoggerMessage` methods |
| 2 | Validation | Runs the FluentValidation validators for the request; a failure returns a validation `Result` without entering the handler |
| 3 (innermost) | Unit of work | Opens the transaction, and commits once after a command handler returns success |

Logging is outermost so that a request rejected by validation is still logged with
its duration — a burst of validation failures is a signal, and a behaviour that
never sees them cannot report it. The unit of work is innermost because it must be
the last thing to open and the first thing to close.

**Handlers never save.** The pipeline commits after a command handler returns
success, and rolls back on failure, so a handler that gives up halfway cannot leave
a half-applied change behind and cannot forget to persist one. Queries skip the
unit of work entirely; there is nothing to commit, and wrapping a read in a
transaction only holds a connection open for no benefit.

Failure is a return value throughout. Handlers return `Result` or `Result<T>`
carrying an `Error`, because "this submission does not exist" is a normal outcome of
a lookup rather than an exceptional one. Exceptions are reserved for programmer
error.

## Documentation

| Document | Contents |
| --- | --- |
| [docs/guide.md](docs/guide.md) | How to use the bot: first-time setup in five steps, the path an application takes, every command explained with its options, what each button and pop-up does, how to read a panel, and what to check when something looks wrong |
| [docs/architecture.md](docs/architecture.md) | Layer diagram, the dependency rule and why it points inward, what belongs in each project, the CQRS pipeline, why PostgreSQL over MySQL |
| [docs/domain.md](docs/domain.md) | Entity relationships, submission and cycle lifecycles, the exact voting arithmetic, the Approved/Rejected/Skipped distinction, the short-code system and its collision reasoning |
| [docs/configuration.md](docs/configuration.md) | Every configuration key with its default and whether it is required, the precedence chain, the `__` separator, bot tokens, intents, and OAuth scopes |
| [docs/operations.md](docs/operations.md) | Running locally, migrations, health endpoints, what to monitor, OpenTelemetry, log levels, reading the scheduler, backup and restore, troubleshooting |
| [docs/interactions.md](docs/interactions.md) | Every slash command with its options and required permissions, and where each button, select menu, modal, and autocomplete handler appears |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for branching, commit conventions, running
the formatter and tests before pushing, and the analyzer policy — warnings are
advisory locally and fatal in CI, deliberately.

Notable changes are recorded in [CHANGELOG.md](CHANGELOG.md), following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Licence

MIT. See [LICENSE](LICENSE).
