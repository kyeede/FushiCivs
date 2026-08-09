# Operations

Running Fushi, watching it, and fixing it when it misbehaves.

## Deploying to a VPS (Debian + Docker)

The production path keeps **source off the server**. You build the image on your
machine, ship only the image plus two small files, and Docker keeps the bot up
across crashes and reboots (`restart: unless-stopped`).

### What you need on the server

- Debian with Docker working (`sudo docker run hello-world` succeeds).
- The Compose plugin: `sudo docker compose version` (V2). If that fails, install
  `docker-compose-plugin` from Docker's Debian packages — the older
  `docker-compose` hyphen binary is not what these files target.
- Outbound HTTPS to Discord. No inbound ports are required for the bot itself.

### On your development machine

```bash
# From the repository root
docker build -f build/Dockerfile -t fushi:1.0 .
docker save fushi:1.0 | gzip > fushi-1.0.tar.gz

scp fushi-1.0.tar.gz \
    docker-compose.prod.yml \
    .env.production.example \
    aquila@YOUR_SERVER_IP:~/fushi/
```

Prefer an SSH key over password auth for that `scp`/`ssh` step. Password login on
a public IP gets brute-forced continuously; keys do not change how the bot runs.

### On the server

```bash
mkdir -p ~/fushi && cd ~/fushi
gunzip -c fushi-1.0.tar.gz | sudo docker load

cp .env.production.example .env
nano .env   # set Discord__Token and POSTGRES_PASSWORD (openssl rand -base64 32)

sudo docker compose -f docker-compose.prod.yml up -d --wait
sudo docker compose -f docker-compose.prod.yml logs -f bot
```

You should see the gateway connect, then a registration pass creating the guild
row. Slash commands registered globally can take up to about an hour to appear on
first deploy; set `Discord__DevelopmentGuildId` to your test guild snowflake if
you want them instantly while you verify.

### Day-to-day

```bash
sudo docker compose -f docker-compose.prod.yml ps
sudo docker compose -f docker-compose.prod.yml logs -f --tail=100 bot
sudo docker compose -f docker-compose.prod.yml restart bot
sudo docker compose -f docker-compose.prod.yml pull   # only if you use a registry
```

Updating to a new build is the same as the first ship: build and save locally,
`scp` the new archive, `docker load`, then:

```bash
sudo docker compose -f docker-compose.prod.yml up -d
```

Compose recreates the bot container when the image tag changes. The Postgres
volume is left alone.

### What stays off the server

The git tree, your Discord token in chat, and the development `.env`. The server
holds: the loaded image, `docker-compose.prod.yml`, and a filled-in `.env` with
mode `600` (`chmod 600 .env`).

## Running locally

The compose stack starts PostgreSQL only by default. Redis and a database browser
sit behind profiles, because neither is needed to run the bot and starting them
unconditionally just makes `up` slower.

```bash
cp .env.example .env                 # then fill in Discord__Token
docker compose up -d --wait          # PostgreSQL
build/migrate.sh update              # apply the schema
set -a && . ./.env && set +a         # export configuration
dotnet run --project src/Fushi.Host
```

`--wait` blocks until the health check passes, so the migration step does not race
a database that has started but is not yet accepting connections.

Optional tiers:

```bash
docker compose --profile cache up -d            # + Redis
docker compose --profile tools up -d            # + Adminer on http://localhost:8080
docker compose --profile cache --profile tools up -d
```

Adminer connects with the values from `.env`: system PostgreSQL, server
`postgres`, and the `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` values.

Stopping:

```bash
docker compose --profile cache --profile tools down    # keeps the data volume
docker volume rm fushi-postgres-data                   # deliberate reset
```

`down` without `--volumes` on purpose. Losing a development database to a
reflexive `down` is a bad afternoon, and wiping it should take a second command
that you have to mean.

For iterative work, `dotnet watch --project src/Fushi.Host run` restarts on
change. Note that a restart drops the gateway session; doing it repeatedly in
quick succession will eventually hit Discord's identify rate limit, and the bot
will sit reconnecting for a few minutes. Editing a handler is fine; saving on
every keystroke is not.

## Migrations

`build/migrate.sh` wraps `dotnet ef` with the right project pair every time.
Migrations live in `src/Fushi.Infrastructure`; the host is the startup project,
because that is what knows how to build the configuration the context needs.

```bash
build/migrate.sh add AddSubmissionCodeIndex   # scaffold
build/migrate.sh list                         # what exists, and what is applied
build/migrate.sh update                       # apply everything pending
build/migrate.sh update 0                     # unapply everything
build/migrate.sh remove                       # delete the newest, if unapplied
build/migrate.sh script > artifacts/change.sql
```

Read every generated migration before committing it. EF infers intent from a model
diff and cannot tell a rename from a drop followed by an add — and the difference
between those two is whether the column's data survives.

### Applying migrations in production

Do not let the application migrate itself on startup. Two instances starting
together will both try, and the loser fails in a way that is hard to reason about
after the fact.

Generate idempotent SQL, review it, and apply it as a deliberate step:

```bash
build/migrate.sh script > migration.sql
# review, then apply with psql during a maintenance window
```

`--idempotent` (which the script always passes) guards every statement against the
migrations history table, so the same file is safe to run against a database at
any revision.

Time the migration against a copy of production data first if it touches a large
table. A migration that takes a lock for thirty seconds will drop votes cast in
that window — the bot will report an error to the voter rather than silently
losing it, but it is still thirty seconds of a cycle nobody can vote in. Schedule
schema changes outside the configured voting windows.

## Health

The host reports health through `Microsoft.Extensions.Diagnostics.HealthChecks`
over a small HTTP listener, with two endpoints that answer different questions:

| Endpoint | Question | Use for |
| --- | --- | --- |
| `/health/live` | Is the process alive and not deadlocked? | Liveness probe — restart on failure |
| `/health/ready` | Can it actually do its job right now? | Readiness probe, alerting |

The distinction matters. Liveness failing should restart the process. Readiness
failing should *not*: if the database is down, restarting the bot does not bring
the database back and only adds a cold start to the outage.

Readiness aggregates three checks:

| Check | Healthy when | Degraded / unhealthy when |
| --- | --- | --- |
| Gateway | The Discord session is connected and identified | Reconnecting, rate-limited, or the token was rejected |
| Database | A trivial query succeeds | The connection pool is exhausted or PostgreSQL is unreachable |
| Cache | L1 responds; L2 responds if configured | Redis is configured but unreachable — degraded, not unhealthy, because L1 alone still works |

A Redis outage is deliberately not fatal. `HybridCache` falls back to its
in-process tier, which is correct for a single instance and merely stale-prone
across several.

## What to monitor

Ordered by how much you will regret not having it.

**Gateway connection state.** Everything else is downstream of this. A bot that is
disconnected is not failing loudly — it is doing nothing, quietly. Alert on a
session that has been disconnected for more than a couple of minutes.

**Scheduler activity.** A cycle that should have opened and did not is the failure
this system is most likely to have, and the one nobody notices until someone asks
why voting is closed on a Wednesday. Alert on "no cycle opened within fifteen
minutes of a scheduled opening".

**Interaction failure rate.** Discord requires an interaction to be acknowledged
within three seconds. A handler that starts exceeding that shows up to users as
"The application did not respond" with no other signal. Watch the latency
distribution, not just the mean.

**Database connection pool saturation and query latency.** The usual reasons: a
missing index after a schema change, or a query that got slower as a table grew.

**Rate-limit responses from Discord.** Occasional 429s are normal and handled.
A sustained rate is a bug — something is retrying in a loop.

**Unhandled exceptions.** Should be zero. Handlers return `Result` for expected
failures, so anything reaching the top as an exception is genuinely unexpected.

## OpenTelemetry

Wired through `OpenTelemetry.Extensions.Hosting`, with the OTLP exporter and
runtime instrumentation. Configure it with the standard OTEL environment
variables:

| Variable | Effect |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Collector address. Unset disables export. |
| `OTEL_SERVICE_NAME` | Service name attached to every signal. Defaults to `fushi`. |

With no endpoint set, instrumentation still runs — the cost of *collecting* is
paid either way — but nothing leaves the process. That is the right development
configuration: you can still read the numbers from the logs without running a
collector.

Runtime instrumentation gives you GC pause counts and durations, heap size by
generation, thread pool queue length, and exception counts. Thread pool queue
length is the one to watch: it is the earliest signal that interaction handlers
are blocking, and it climbs before the three-second acknowledgement deadline
starts being missed.

Traces are emitted per command through the CQRS pipeline's logging behaviour, so a
slow interaction can be attributed to validation, the handler, or the transaction
commit rather than just "slow".

## Logging

Serilog, to the console in a container and additionally to a rolling file where
one is configured. Levels are set through the standard `Logging__LogLevel__*`
configuration keys.

| Level | What lands here | When to run at it |
| --- | --- | --- |
| `Error` | Unhandled exceptions, failed gateway authentication, failed commits | Never as a floor — you lose the context that explains the error |
| `Warning` | Rate limits, retries, degraded cache, permission failures | Quiet production, if you have metrics covering the rest |
| `Information` | Cycle opened / closed / finalised, submissions decided, configuration changes | **Default.** The narrative of what the bot did |
| `Debug` | Every command handled with its outcome and duration, cache hits and misses, each scheduler tick | Diagnosing a specific problem |
| `Verbose` | Gateway event payloads | Briefly, and never with real user content in a shared log |

Narrow rather than lower: turning the whole application to `Debug` on a busy
instance produces more output than you can read.

```
Logging__LogLevel__Default=Information
Logging__LogLevel__Fushi.Host.Scheduling=Debug
```

All hot-path logging goes through source-generated `LoggerMessage` methods in
`Fushi.Application/Logging`, so a disabled level costs a comparison and allocates
nothing. Leaving `Debug` categories configured but disabled is free.

## Guild registration

Every other feature hangs off a guild's configuration row, so something has to
create one. `GuildRegistrar` is a hosted service that asks Discord which guilds the
bot is actually in and gives a row to any that lack one. The first pass runs as
soon as the gateway reports ready; after that it runs on a timer.

It is convergent for the same reason the scheduler is. Reacting only to the
guild-join event would cover a server added while the bot happens to be running and
miss every other case — one added during a restart, during an outage, or before the
service existed would never be registered, and nothing would ever notice. Asking
what the bot is in cannot develop that blind spot.

Two properties are worth knowing:

**It is additive.** A guild the bot has been removed from keeps its row. The only
evidence of a departure is an absence from the list, and an absence is exactly what
a reconnect manufactures for every guild at once. A stale row costs a row; acting on
a false one would delete a server's channels, schedule, and voting grants, and
nothing in Discord could restore them. Prune deliberately if you ever need to.

**It refuses to guess.** When the gateway is between sessions the socket cache is
empty, and `DiscordGuildDirectory` reports a failure rather than an empty list. The
pass logs at warning and does nothing. This is nearly always a reconnect resolving
itself, but a rejected token looks identical from here and does not resolve itself,
which is why it is not logged silently.

New rows are stamped with actor `0`, the system actor, because nobody asked for
them. A pass that creates nothing — the normal case after the first — logs at debug,
so the information level stays a record of guilds actually being taken on.

| Setting | Default | Effect |
| --- | --- | --- |
| `Scheduler__RegistrationSeconds` | `300` | Seconds between passes |
| `Scheduler__RegistrationEnabled` | `true` | Whether passes run at all |

Five minutes is the longest of the three intervals on purpose: a pass costs one
keyed lookup per guild and finds nothing to do on every run after the first.

## Reading the scheduler

The scheduler is a hosted service that wakes periodically and, for each
operational guild, asks the same three questions: should a cycle be open now,
should an open one be closed, and should a closed one be finalised.

Understanding two properties will explain most of what looks odd:

**It is convergent, not event-driven.** It does not schedule a timer for the exact
opening instant; it compares the current time against the guild's resolved window
each pass. So a cycle can open slightly *after* its nominal time — up to one tick.
That is expected. A process restart, a clock jump, or a missed tick self-corrects
on the next pass, which is worth far more than punctuality to the second.

**Transitions are idempotent.** Moving a cycle to the state it is already in is a
no-op. If the process dies between opening a cycle and recording that it opened,
the next pass completes the job rather than failing.

Consequences worth knowing before you file a bug:

- A vote cast between the closing instant and the scheduler noticing is
  **rejected**. `IsAcceptingVotes` requires both the status and the clock to
  agree, and the clock is authoritative.
- A guild with `IsOperational` false is skipped silently at `Debug` level. If
  cycles are not opening, check the intake and review channels are both set and
  the guild is enabled — that is far more often the cause than a scheduler fault.
- A guild whose `CycleDays` is `None` never opens a cycle. That is the intended
  way to pause without discarding configuration, not a fault.
- Around a daylight saving transition, a window's duration is legitimately an hour
  longer or shorter than the configured times suggest. See
  [domain.md](domain.md#scheduling).

## Backup and restore

The database holds everything: configuration, voting grants, submissions, votes,
and the audit trail. None of it is reconstructible from Discord — review messages
are the bot's own output, not its source of truth.

### Backup

```bash
docker compose exec -T postgres \
  pg_dump --username=fushi --format=custom --clean --if-exists fushi \
  > "backups/fushi-$(date +%Y%m%d-%H%M%S).dump"
```

`--format=custom` rather than plain SQL: it compresses, it can be restored
selectively, and `pg_restore` can parallelise it. `--clean --if-exists` makes the
dump self-contained for a restore over an existing database.

For a real deployment, run this on a schedule, ship the result off the machine
holding the database, and **test a restore periodically**. An untested backup is
a hypothesis.

### Restore

```bash
docker compose exec -T postgres \
  pg_restore --username=fushi --dbname=fushi --clean --if-exists \
  < backups/fushi-20260807-120000.dump
```

Stop the bot first. Restoring underneath a running instance gives it a cache full
of rows that no longer exist and, worse, lets it write to a database mid-restore.

After restoring, run `build/migrate.sh list` before starting the bot: if the dump
predates a migration that has since been applied, the schema is behind the code
and the application will fail in confusing ways. Apply the pending migrations
first.

### Point-in-time recovery

The compose setup does not configure WAL archiving. If you need to recover to an
arbitrary instant rather than to the last nightly dump, use a managed PostgreSQL
service or configure `archive_mode` and a base backup schedule — both are beyond
what a development compose file should be doing.

## Troubleshooting

### `TimeZoneNotFoundException: The time zone ID 'Europe/Berlin' was not found`

The single most likely serious misconfiguration, and the reason
`InvariantGlobalization` is `false` in `Directory.Build.props` with a comment
telling you not to change it.

Under invariant globalization, .NET ships no time zone data and *every*
`TimeZoneInfo.FindSystemTimeZoneById` call throws, whatever identifier you pass.
Fushi resolves `Europe/Berlin` (or whatever a guild configured) every time it works
out a voting window, so the exception surfaces when the scheduler tries to open or
close a cycle — not at startup. The symptom is "the bot silently stopped running
votes", which is a long way from the cause.

Check, in order:

1. `InvariantGlobalization` is `false` in `Directory.Build.props`. It is, unless
   someone changed it.
2. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` is not set to `1` in the environment.
   This environment variable overrides the build property at runtime and is
   sometimes added to containers as a size optimisation.
3. The container has ICU and tzdata. `build/Dockerfile` uses the Ubuntu-based
   runtime image (which carries ICU) and installs `tzdata` explicitly. Switching
   to an Alpine or distroless base without adding `icu-libs` and `tzdata`
   reintroduces exactly this failure.

Confirm from inside the container:

```bash
docker compose exec bot printenv | grep -i globalization   # expect no output
docker compose exec bot ls /usr/share/zoneinfo/Europe/Berlin
```

On Windows, note that `TimeZoneInfo` accepts IANA identifiers through ICU, so
`Europe/Berlin` works there too — but only with globalization enabled. The same
fault, the same fix.

### `/config` replies "This server has not been set up yet"

The guild has no configuration row. Since the reply names `/config channels` and
that command reads before it writes, the advice is circular — the command it points
at fails the same way.

`GuildRegistrar` normally makes this unreachable by creating the row within seconds
of the gateway connecting. Seeing it means one of:

- The bot was added to the server less than one registration interval ago, and the
  next pass has not run yet. Wait, or restart the bot to force a pass immediately.
- `Scheduler__RegistrationEnabled` is `false`, so nothing creates rows in the
  background. Any configuration command that writes will still create one.
- The registration pass is failing. Look for event 4014 at warning level, which
  carries the reason.
- The database was wiped underneath a running process. The row is recreated on the
  next pass, not on the next command.

### The bot starts but no slash command appears

Almost always one of three things:

- The invite was generated without the `applications.commands` scope. Re-invite
  with both `bot` and `applications.commands`; removing and re-adding the bot is
  not necessary, visiting the corrected invite URL is enough.
- `Discord__DevelopmentGuildId` is set to a different guild than the one you are
  testing in. Commands registered to a guild appear only there.
- It is unset, so commands registered globally, and global propagation has not
  finished. Set it to your test guild during development.

### Submissions are not being captured from the intake channel

- `MessageContent` is a privileged intent and must be enabled in the developer
  portal. Without it, message events arrive with empty content and there is
  nothing to capture. This does not produce an error — it produces silence.
- The bot needs **View Channel** and **Read Message History** on the intake
  channel specifically. A channel-level override can deny what the role grants.
- Check the intake channel is actually configured: `/config channels` shows the
  current routing.

### `Npgsql.NpgsqlException: Connection refused`

- PostgreSQL is not running: `docker compose ps` should show it healthy.
- The connection string points at `localhost` from inside a container, where it
  should be the compose service name `postgres`.
- `POSTGRES_HOST_PORT` was changed but the connection string still names 5432.

### A migration fails with "relation already exists"

The database is ahead of the migrations history — usually because a schema change
was applied by hand, or a dump was restored from a database at a different
revision. `build/migrate.sh list` shows what EF believes is applied. Reconcile
deliberately; do not delete migrations to make the error go away.

### Interactions fail with "The application did not respond"

Discord's three-second acknowledgement deadline was missed. Look at the command
duration in the pipeline's logs at `Debug` to see which stage is slow: validation,
the handler, or the commit. The usual cause is a database query that has degraded
as a table grew.

### A cycle did not open

In likelihood order:

1. The guild is not operational — disabled, or missing the intake or review
   channel. Check `/config` output.
2. The guild's `CycleDays` does not include today.
3. The bot was disconnected from the gateway across the opening instant. Because
   the scheduler is convergent, it will open the cycle on the next pass after
   reconnecting, late but not lost.
4. The time zone resolved to something unexpected — see the
   `TimeZoneNotFoundException` section, and check the guild's configured zone with
   `/config schedule`.

### Warnings fail the build in CI but not locally

Working as designed. `Directory.Build.props` sets `TreatWarningsAsErrors` only
when `ContinuousIntegrationBuild` is true, which the GitHub Actions runner turns
on by setting `GITHUB_ACTIONS=true`. Reproduce the CI behaviour locally with:

```bash
GITHUB_ACTIONS=true build/build.sh
```
