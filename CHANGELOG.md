# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Because Fushi is a deployed application rather than a library, the public surface
that semantic versioning describes is taken to be the slash command surface, the
configuration keys, and the database schema. A change that forces a guild to
reconfigure something, or that changes what an existing command does, is breaking
even if no compiled API changed.

<!--
Add entries under Unreleased as work lands, not at release time. The categories
are the Keep a Changelog set: Added, Changed, Deprecated, Removed, Fixed,
Security. Omit a category rather than writing "None" under it.

Note that .gitattributes sets `merge=union` for this file, so concurrent
branches appending to the same section merge without a conflict. Check the
result reads sensibly after a merge; union means both sides are kept, not that
the outcome is ordered.
-->

## [Unreleased]

The initial architecture. Nothing has been released yet, so everything below is
first-time work rather than a change to previous behaviour.

### Added

**Domain model (`Fushi.Core`)**

- `Guild`, keyed by the Discord guild snowflake rather than a generated identifier,
  carrying channel routing, voting policy, cycle schedule, and enabled state. A row
  is created when the bot joins, so every setting has a working default and
  `IsOperational` decides whether a cycle can run.
- `Submission` with its lifecycle (`Draft`, `Queued`, `UnderReview`, `Decided`,
  `Withdrawn`) and transitions enforced on the entity. A submission outlives the
  cycle judging it and returns to the queue if that cycle is cancelled.
- `Cycle` with its lifecycle (`Scheduled`, `Open`, `Closed`, `Finalised`,
  `Cancelled`). A cycle copies the voting policy and the resolved window at
  creation, so rescheduling or raising the pass threshold cannot change the terms
  of a vote already under way.
- `Vote` and `VoteTally`, distinguishing deciding votes (approvals plus rejections)
  from total votes. Retraction is a soft delete, so the arithmetic ignores it while
  the audit trail keeps it.
- `VotingPolicy`, owning the single answer to "did it pass": quorum and approval
  ratio as independent gates, defaulting to 3 deciding votes and 60%. Failing
  quorum yields `Skipped` rather than `Rejected`, because "nobody looked at it" and
  "the panel said no" are different facts and only one should count against an
  applicant. Abstentions are excluded from both the ratio and the quorum.
- `CycleSchedule` and `CycleWindow`, resolving a recurring wall-clock rule onto
  absolute instants. Time zones are stored as IANA identifiers, defaulting to
  `Europe/Berlin`, and both daylight saving edge cases are handled explicitly: a
  local time that never happened steps forward to the first that did, and one that
  happened twice takes the earlier instant for opening and the later for closing.
- `VotingPermission` and `VotingPermissionScope`, implementing deny-by-default
  voting rights granted additively to users or roles, with no deny rule. Roles are
  resolved at the moment of the vote rather than stored.
- `ShortCode` and `ShortCodeAlphabet`: six-character Crockford Base32 public codes
  over a 2^30 space, with `I` and `L` folding to `1` and `O` to `0` on input, and
  the all-zero value reserved to mean "unassigned". Generated from a cryptographic
  source, unique per guild, with a reassignment path for the collision retry that
  refuses to act once a code has been published.
- `AuditEntry`, `AuditAction`, and `AuditScope`: an append-only trail with
  explicitly numbered, never-reused action values, copying the subject's short code
  so an entry stays readable after the record it refers to is gone.
- `Result`, `Result<T>`, `Error`, and `ErrorType`, so expected failures travel as
  return values rather than exceptions.
- Discord mention and snowflake utilities, timestamp styles, and paging primitives
  (`Page<T>`, `PageRequest`, `PageInfo`).

**Application layer (`Fushi.Application`)**

- CQRS commands, queries, and handlers organised by feature. No `*Service` classes.
- An in-house dispatcher, replacing MediatR after its move to a commercial licence.
  It resolves a handler per request type and caches a closed-generic executor, so
  the reflection needed to bridge a run-time request type to a statically typed
  handler is paid once per request type rather than once per request.
- A three-stage pipeline, outermost first: logging, validation, unit of work.
  Handlers never save — the pipeline commits after a command handler returns
  success and rolls back on failure. Queries skip the unit of work.
- FluentValidation validators, and handler and validator discovery by assembly
  scan at startup.
- Source-generated `LoggerMessage` logging, one file per feature under `Logging/`,
  so a disabled log level allocates nothing.
- The outward-facing abstractions this layer declares and the infrastructure
  implements: persistence, messaging, and Discord.

**Infrastructure (`Fushi.Infrastructure`)**

- EF Core 11 on PostgreSQL through Npgsql, with entity configurations, value
  converters for short codes and snowflakes, repositories, and the unit of work.
- `HybridCache` with an in-process L1 tier and an optional Redis L2 tier, selected
  by whether `ConnectionStrings__Redis` is set.
- An injected clock, so scheduling behaviour around a daylight saving transition is
  testable without waiting for October.

**Repository scaffolding**

- Central package management, with the `Microsoft.Extensions.*` and EF Core
  versions pinned to the SDK band in `global.json`.
- `docker-compose.yml`: PostgreSQL 17 healthchecked and pinned to UTC, with Redis
  and Adminer behind `cache` and `tools` profiles.
- `build/Dockerfile`: multi-stage, publishing from the SDK preview image to the
  runtime preview image, running as a non-root user, with ICU and tzdata present
  because `Europe/Berlin` resolution depends on them.
- `build/build.sh` and `build/migrate.sh`.
- CI on GitHub Actions: restore, format verification, Release build, and tests on
  Microsoft.Testing.Platform against a PostgreSQL service container, plus CodeQL
  and Dependabot.
- Documentation covering the architecture, the domain rules, every configuration
  key, operations, and the full interaction surface.

### Security

- Voting is denied by default and opened only by explicit grant, so a
  misconfiguration locks people out rather than silently admitting the wrong
  voters.
- Short codes come from a cryptographically secure random source rather than
  `System.Random`, because a predictable sequence would let someone enumerate
  moderation records that were never shared with them.
- `nuget.config` clears the inherited feed list and maps a single source, so an
  added feed cannot shadow a package from nuget.org.
- No credential appears in a tracked file. `.env.example` carries names without
  values, and the VS Code launch profiles reference `.env` rather than inlining
  anything.

<!--
Release procedure, for whoever cuts the first one:

1. Move the Unreleased entries under a new `## [x.y.z] - YYYY-MM-DD` heading.
2. Leave an empty Unreleased section above it.
3. Update VersionPrefix in Directory.Build.props to match.
4. Add the comparison links at the bottom.
5. Tag the commit `vx.y.z`.
-->

[Unreleased]: https://github.com/fushi/fushi/commits/master
