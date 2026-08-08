# Configuration

Every setting Fushi reads, where it comes from, and what happens if you leave it
out. `.env.example` is the tracked template; copy it to `.env` and fill it in.

## How configuration is resolved

Sources are layered, each overriding the ones before it:

```
appsettings.json  <  appsettings.{Environment}.json  <  user secrets  <  environment variables
```

1. **`appsettings.json`** — defaults committed to the repository. Nothing secret.
2. **`appsettings.{Environment}.json`** — where `{Environment}` is the value of
   `DOTNET_ENVIRONMENT`, typically `Development` or `Production`. Also committed,
   also nothing secret.
3. **User secrets** — a per-developer JSON file outside the repository, keyed to
   the project. The right place for a development bot token, because it cannot be
   committed by accident.
4. **Environment variables** — highest precedence, and how production is
   configured. A variable set here wins over everything above it.

Later sources override earlier ones **key by key**, not file by file. Setting
`Discord__Token` in the environment does not discard the rest of the `Discord`
section from `appsettings.json`.

### The `__` separator

.NET configuration is hierarchical, but environment variables are flat. A double
underscore in a variable name is the section separator:

| Environment variable | Configuration path |
| --- | --- |
| `Discord__Token` | `Discord:Token` |
| `ConnectionStrings__Database` | `ConnectionStrings:Database` |
| `Logging__LogLevel__Default` | `Logging:LogLevel:Default` |

Two underscores, not one, and not a colon. A colon works on Linux but is invalid
in an environment variable name on Windows, which is why `__` is the portable
form and the only one used here.

Variables without a `__` — `POSTGRES_DB`, `OTEL_SERVICE_NAME` — are not read by
.NET configuration at all. They are consumed by docker-compose or by the
OpenTelemetry SDK, which have their own conventions.

## Reference

### Discord

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `Discord__Token` | **Yes** | none | Bot token. A credential: anyone holding it controls the bot account. |
| `Discord__DevelopmentGuildId` | No | empty | Restricts slash-command registration to one guild. Leave empty in production. |

`Discord__DevelopmentGuildId` exists because global slash commands take time to
propagate across Discord, while guild-scoped commands appear immediately. During
development that is the difference between iterating in seconds and iterating in
an hour. Set it to your test server's ID; leave it empty and commands register
globally.

### Database

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings__Database` | **Yes** | none | Npgsql connection string. |

The development default, matching what `docker-compose.yml` provisions:

```
Host=localhost;Port=5432;Database=fushi;Username=fushi;Password=fushi_dev
```

Inside the compose network the host is the service name rather than `localhost`:

```
Host=postgres;Port=5432;Database=fushi;Username=fushi;Password=fushi_dev
```

In production, add `SSL Mode=Require` (or `VerifyFull` where you control the
certificate chain) and use a role with only the privileges the application needs
— it does not need `CREATEDB` or superuser.

### Cache

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `ConnectionStrings__Redis` | No | empty | Empty disables the distributed cache tier. |

Caching uses `HybridCache`, which has an in-process L1 tier and an optional
distributed L2 tier. With this key empty, L1 runs alone: everything still works,
and for a single instance that is the right configuration — an in-process
dictionary is faster than a network round trip.

Set it once you run more than one instance, at which point the L2 tier is what
stops instance A serving a guild's configuration that instance B has already
changed. A typical value: `localhost:6379`, or `redis:6379` inside compose.

### Runtime

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `DOTNET_ENVIRONMENT` | No | `Production` | Selects `appsettings.{Environment}.json`. |

Set to `Development` locally. The host treats `Development` as the signal to
enable developer-oriented behaviour, so do not set it in production even
temporarily.

### Telemetry

| Key | Required | Default | Notes |
| --- | --- | --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | No | empty | Empty disables the OTLP exporter. |
| `OTEL_SERVICE_NAME` | No | `fushi` | Identifies this instance in traces and metrics. |

These are read by the OpenTelemetry SDK using its own environment variable
convention, which is why they have no `__`. With no endpoint set, instrumentation
still runs and metrics are still recorded locally; nothing is shipped anywhere.
See [operations.md](operations.md#opentelemetry).

### docker-compose only

Not read by the application. These configure the containers in
`docker-compose.yml`.

| Variable | Default | Purpose |
| --- | --- | --- |
| `POSTGRES_DB` | `fushi` | Database created on first boot |
| `POSTGRES_USER` | `fushi` | Role created on first boot |
| `POSTGRES_PASSWORD` | `fushi_dev` | That role's password |
| `POSTGRES_HOST_PORT` | `5432` | Host port PostgreSQL is published on |
| `REDIS_HOST_PORT` | `6379` | Host port Redis is published on |
| `ADMINER_HOST_PORT` | `8080` | Host port Adminer is published on |

Changing `POSTGRES_USER`, `POSTGRES_PASSWORD`, or `POSTGRES_DB` after the volume
has been initialised has no effect: PostgreSQL only reads them when it creates the
data directory. Delete the `fushi-postgres-data` volume to start over.

Changing `POSTGRES_HOST_PORT` changes only the host side of the mapping. The port
inside the compose network is always 5432, so a bot running in the same network is
unaffected while one running on the host needs its connection string updated.

## Per-guild settings

These are not configuration files. They are stored per guild and changed with
`/config` (see [interactions.md](interactions.md)). Listed here because "where is
the quorum set" is a configuration question even though the answer is not an
environment variable.

| Setting | Default | Range | Meaning |
| --- | --- | --- | --- |
| Approval ratio | `0.60` | 0.0–1.0 | Share of deciding votes that must approve. Compared inclusively. |
| Quorum | `3` | ≥ 0 | Minimum deciding votes for a result to count. 0 disables the gate. |
| Allow abstain | `true` | — | Whether `Abstain` is offered. Abstentions never affect ratio or quorum. |
| Allow self-vote | `false` | — | Whether an applicant may vote on their own submission. |
| Allow vote change | `true` | — | Whether a voter may revise a vote while the cycle is open. |
| Cycle days | Mon, Wed, Sat | any subset | Days a cycle opens. Empty means the guild is paused. |
| Opens at | `10:00` | — | Local wall-clock time voting opens. |
| Closes at | `22:00` | — | Local wall-clock time voting closes. At or before *opens at* means overnight. |
| Time zone | `Europe/Berlin` | any IANA ID | Never an offset. See [domain.md](domain.md#scheduling). |

Channel routing is also per guild: intake and review are required before a cycle
can open; results, archive, and log are optional. An unset results channel falls
back to the review channel.

A note on the approval ratio: a configured value of exactly `0` is currently read
as "unconfigured" and falls back to `0.60`. Use a small positive value if you
genuinely want everything that reaches quorum to pass.

## Setting up

### Development

```bash
cp .env.example .env
# fill in Discord__Token and Discord__DevelopmentGuildId
```

Then either export it into your shell:

```bash
set -a && . ./.env && set +a
```

…or let a launch configuration do it — `.vscode/launch.json` points `envFile` at
`.env`, so debugging from the editor picks it up without any shell setup.

`.env` is gitignored. `.env.example` is tracked, and every new variable belongs
there (without its value) at the same time it is introduced.

If you would rather not have a token in a dotfile at all, use user secrets:

```bash
dotnet user-secrets --project src/Fushi.Host init
dotnet user-secrets --project src/Fushi.Host set "Discord:Token" "<token>"
```

Note the colon rather than `__`: user secrets are JSON configuration, not
environment variables. This stores the token outside the repository entirely,
keyed to the project, which is the only arrangement where committing it is
impossible rather than merely discouraged.

### Production

Set environment variables through whatever your platform provides — systemd
`EnvironmentFile`, Kubernetes `Secret`, a container platform's secret store. Do
not ship a `.env` file, and do not bake the token into an image: an image layer is
readable by anyone who can pull it.

A minimal production environment:

```
DOTNET_ENVIRONMENT=Production
Discord__Token=<from your secret store>
ConnectionStrings__Database=Host=db.internal;Port=5432;Database=fushi;Username=fushi;Password=<secret>;SSL Mode=Require
ConnectionStrings__Redis=redis.internal:6379
OTEL_EXPORTER_OTLP_ENDPOINT=http://collector.internal:4317
OTEL_SERVICE_NAME=fushi
```

Leave `Discord__DevelopmentGuildId` unset so commands register globally.

## Getting a bot token

1. Open the [Discord Developer Portal](https://discord.com/developers/applications)
   and create a new application.
2. Go to **Bot** and create the bot user.
3. **Reset Token**, then copy it. Discord shows it once. If you lose it, reset
   again — you cannot retrieve the old one.
4. Put it in `Discord__Token`.

Treat the token exactly as you would a password: it *is* the bot account. If one
leaks, reset it in the portal immediately; that invalidates the old one.

### Gateway intents

Enable these under **Bot → Privileged Gateway Intents** and in the bot's intent
configuration:

| Intent | Privileged | Why Fushi needs it |
| --- | --- | --- |
| `Guilds` | No | Guild, channel, and role state. Without it the bot cannot resolve the channels it is configured with. |
| `GuildMessages` | No | Notices messages posted in the intake channel. |
| `MessageContent` | **Yes** | Reads the body of those messages. Without it, message events arrive with empty content and no submission can be captured. |

`MessageContent` is privileged. It must be switched on explicitly in the developer
portal, and once your bot is in more than 100 servers it requires verification and
approval from Discord. Enabling it in code alone is not enough: the gateway
rejects the connection if the code requests an intent the application has not been
granted.

Fushi requests only these three. It does not need `GuildMembers` — voter role
checks use the roles carried on the interaction rather than a cached member list.

### OAuth scopes and permissions

Generate the invite URL under **OAuth2 → URL Generator** with these scopes:

| Scope | Why |
| --- | --- |
| `bot` | Adds the bot user to the server. |
| `applications.commands` | Allows it to register slash commands. Without this, the bot joins but no command appears. |

Channel permissions it needs where it operates:

| Permission | Where | Why |
| --- | --- | --- |
| View Channel | intake, review, results, archive, log | Cannot act on a channel it cannot see. |
| Read Message History | intake | Captures submissions posted before it connected. |
| Send Messages | review, results, archive, log | Posts review messages and announcements. |
| Embed Links | review, results, archive | Submissions and results are rendered as embeds. |
| Create Public Threads | review | Optional. Only if discussion threads are in use. |
| Send Messages in Threads | review | Optional, same. |

Fushi does not need Administrator, and should not be given it. If a command fails
with a permissions error, the fix is a channel override on the specific channel,
not a broader role.

## Verifying a configuration

```bash
docker compose up -d                 # PostgreSQL
build/migrate.sh update              # schema
set -a && . ./.env && set +a         # environment
dotnet run --project src/Fushi.Host  # start
```

If startup fails, the most common causes in order are: an unset or stale
`Discord__Token`, a database that is not reachable, and a migration that has not
been applied. [operations.md](operations.md#troubleshooting) covers each, along
with the `TimeZoneNotFoundException` that appears if invariant globalization is
ever switched on.
