# Contributing

Thanks for working on Fushi. This document covers how changes get in: branching,
commits, what to run before pushing, and the standards CI enforces.

Read [docs/architecture.md](docs/architecture.md) before a first substantial
change. The layering is enforced by the compiler rather than by review, so knowing
which project a piece of behaviour belongs in saves discovering it as a build
error.

## Getting set up

```bash
git clone <repository-url> fushi
cd fushi
cp .env.example .env                 # fill in Discord__Token
docker compose up -d --wait
build/migrate.sh update
build/build.sh --format
```

You need the SDK version named in `global.json`, Docker, and a POSIX shell for the
scripts in `build/` (Git Bash or WSL on Windows). If you intend to touch the
schema, also `dotnet tool install --global dotnet-ef --prerelease`.

`.vscode/extensions.json` lists the extensions that make the editor agree with CI.
Accepting the recommendation is worth it for `editorconfig.editorconfig` alone —
without it, files outside the C# formatter's reach drift from the layout CI checks.

## Branching

`master` is the default branch and is always releasable. Work happens on a branch
and arrives through a pull request; nothing is pushed to `master` directly.

Name branches `<type>/<short-description>`, using the same types as commits:

```
feat/voter-bulk-grant
fix/skipped-outcome-reported-as-rejected
docs/clarify-quorum-arithmetic
refactor/extract-cycle-window-resolution
```

Rebase onto `master` rather than merging it in. A linear history means
`git log --oneline` reads as a sequence of decisions, and `git bisect` works.
Force-pushing your own branch during review is fine and expected; force-pushing
`master` is not.

Keep branches short-lived. A branch open for three weeks is a merge conflict with
interest.

## Commits

Conventional Commits:

```
<type>(<scope>): <summary>

<body>

<footer>
```

**Types**

| Type | For |
| --- | --- |
| `feat` | New behaviour a user can observe |
| `fix` | A defect corrected |
| `refactor` | Behaviour unchanged, structure changed |
| `perf` | Behaviour unchanged, measurably faster |
| `test` | Tests only |
| `docs` | Documentation only |
| `build` | Build, packaging, dependencies, Docker |
| `ci` | Workflows and CI configuration |
| `chore` | Anything else that touches no production code |

**Scope** is the project or feature area: `core`, `application`, `infrastructure`,
`interactions`, `gateway`, `host`, or a feature name such as `voting`, `cycles`,
`config`. Omit it when a change genuinely spans everything.

**Summary** is imperative mood, lower case, no trailing full stop, under about 72
characters. "add" rather than "added"; the convention reads as an instruction to
the codebase.

**Body** explains *why*. The diff already shows what changed. If a reviewer would
ask "why not the obvious approach", answer it here — that is the part which is
expensive to reconstruct in six months.

**Footer** carries `Closes #123` and breaking-change notices. A breaking change is
marked either with `!` after the type or with a `BREAKING CHANGE:` footer, and for
this project it means a change to the command surface, a configuration key, or the
schema — not only a compiled API.

Examples:

```
fix(core): report a failed quorum as Skipped rather than Rejected

VotingPolicy.Evaluate compared the approval ratio before checking quorum, so a
submission with zero deciding votes fell through to Rejected with a ratio of 0.
An applicant whose submission landed on a quiet week therefore carried a
rejection nobody had voted for.

Closes #42
```

```
feat(config)!: take the approval ratio as a percentage rather than a fraction

/config policy ratio now accepts 60 rather than 0.6. A mistyped 6 is obviously
wrong where a mistyped 0.06 silently sets the threshold to six percent.

BREAKING CHANGE: guilds configured through the previous option must set the
ratio again. Existing stored policies are unaffected; only the command input
changed.
```

Commit in logical units. A commit that renames a type and changes its behaviour is
two commits, because reviewing either one requires ignoring the other. Squash
fixup commits before requesting review — `git rebase -i` is the tool, and nobody
needs to read "fix typo" in the permanent history.

## Before you push

```bash
build/build.sh --format
```

That is exactly what CI runs: restore, `dotnet format --verify-no-changes`, a
Release build, and the test suite. Running it locally is cheaper than finding out
from a red pull request.

Individually, if you want the steps separately:

```bash
dotnet format Fushi.slnx                                   # fix formatting
dotnet format Fushi.slnx --verify-no-changes               # check only
dotnet build Fushi.slnx -c Release
dotnet test Fushi.slnx
```

To reproduce the CI build's strictness locally, set the variable CI sets:

```bash
GITHUB_ACTIONS=true build/build.sh
```

### Tests

Tests run on **Microsoft.Testing.Platform**, not VSTest — `global.json` selects the
runner, and test projects are executables rather than libraries because the
platform requires it. One consequence catches everyone once: **VSTest `--filter`
expressions do not work.** The equivalents are:

```bash
dotnet test Fushi.slnx --filter-class Fushi.Core.Tests.Identifiers.ShortCodeTests
dotnet test Fushi.slnx --filter-method '*Skipped*'
dotnet test Fushi.slnx --filter-trait 'Category=Integration'
dotnet test Fushi.slnx --filter-query '/*/*/ShortCodeTests/*'
```

Assertions use Shouldly and mocks use NSubstitute; both are provided to every test
project automatically by `Directory.Build.targets`, along with `using Xunit` and
`using Shouldly`, so a new test file needs neither a package reference nor those
imports.

`Fushi.Infrastructure.Tests` uses Testcontainers and needs Docker running. The
other two need nothing and run in milliseconds — that is a property worth
protecting, so resist the urge to reach for a database in a test of a domain rule.

**Write the test so it fails first.** A test that passes against the unfixed code
is testing something other than what you think.

### Analyzer policy

Analyzers run at `AnalysisMode=All` with `WarningLevel=9999`, and warnings are:

- **advisory locally** — `TreatWarningsAsErrors` is off, so a half-finished edit
  still builds and runs;
- **fatal in CI** — the GitHub Actions runner sets `GITHUB_ACTIONS=true`, which
  turns on `ContinuousIntegrationBuild`, which turns on `TreatWarningsAsErrors`.

The asymmetry is deliberate. Local work stays runnable mid-thought, and nothing
advisory reaches a shared branch. It does mean a pull request can fail on something
you never saw locally, which is what `GITHUB_ACTIONS=true build/build.sh` is for.

**Do not silence a warning to make a build pass.** Fix the code. If a rule is
genuinely wrong for this codebase, add it to the `NoWarn` list in
`Directory.Build.props` **with a comment justifying it**, in the same style as the
entries already there — each names the rule and says why it is noise rather than
signal here. That list is meant to shrink over time, so adding to it needs an
argument, and a pull request that extends it will be asked for one.

A file-scoped `#pragma warning disable` is acceptable for a genuinely local
exception, but it needs a comment saying why, and it should be re-enabled
immediately after the offending line rather than left open to the end of the file.

### XML documentation

`GenerateDocumentationFile` is on, so **every public type and member carries XML
documentation**. A missing one is warning CS1591 — advisory locally, fatal in CI.

Write documentation that says something the signature does not. Look at the
existing entities for the standard: `<summary>` for what it is, `<remarks>` for why
it is that way and what a caller has to know, `<param>` and `<returns>` for
meaning rather than restatement, and `<exception>` for every exception a caller can
actually provoke. "Gets or sets the value" on a property named `Value` is worse
than nothing, because it satisfies the analyzer while telling the next reader they
have already found the useful documentation.

Test projects are exempt — `Directory.Build.targets` suppresses CS1591 there,
because a test assembly has no public surface a consumer depends on.

### Code conventions

`.editorconfig` covers layout and naming, so most of this is enforced rather than
remembered. The conventions it cannot express:

- **Failure is a return value.** Handlers return `Result` or `Result<T>` with an
  `Error`. Exceptions are for programmer error and genuinely unexpected conditions,
  not for "not found" or "you may not vote here".
- **Entities validate themselves.** A `Submission` cannot be constructed invalid or
  moved to an invalid state. Handlers orchestrate; they do not re-implement a rule
  the entity already owns.
- **No `*Service` classes in `Fushi.Application`.** One command or query per
  operation, with its own handler.
- **Time is injected.** No production code calls `DateTimeOffset.UtcNow`. This is
  not fussiness — the daylight saving behaviour in `CycleSchedule` is only testable
  because of it.
- **Codes, not GUIDs, at the boundary.** Anything a user types or reads is a
  six-character short code.
- **Hot-path logging goes through the source generator** in
  `Fushi.Application/Logging`, one file per feature. This is what justifies
  suppressing CA1848 repository-wide, so bypassing it undermines the suppression.
- **Respect the dependency direction.** If you need something from an outer layer,
  declare an interface in `Fushi.Application/Abstractions` and implement it
  outward. If you find yourself wanting to reference `Fushi.Infrastructure` from
  `Fushi.Application`, the design has gone wrong rather than the rule.

## Pull requests

Fill in the template. The sections that matter most are *Why* and *How this was
verified* — a reviewer cannot exercise a Discord bot from a diff, so say what you
actually ran.

Before requesting review:

- `build/build.sh --format` passes.
- New behaviour has tests, and they fail without the change.
- Public members are documented.
- No new analyzer warnings.
- No token, password, or real guild identifier appears in the diff.
- Documentation is updated in the same pull request when the change affects it:
  `.env.example` and `docs/configuration.md` for a new configuration key,
  `docs/interactions.md` for a command change, `docs/domain.md` for a rule change.
- An entry is added under `## [Unreleased]` in `CHANGELOG.md` for anything a user
  would notice.

Keep pull requests small enough to review properly. A 2,000-line pull request
receives a worse review than four 500-line ones, and the difference shows up later
as a defect nobody caught.

### Schema changes

A migration is reviewed more carefully than the code around it, because it is the
one thing that is awkward to undo.

- Generate it with `build/migrate.sh add <Name>` and **read it**. EF infers intent
  from a model diff and cannot tell a rename from a drop followed by an add — and
  that difference is whether the column's data survives.
- Say in the pull request whether it is reversible, roughly how long it takes on a
  realistic table, and whether it takes a lock that would interrupt a running
  cycle.
- Never edit a migration that has been merged. Add another.

## Reporting things

Open a [bug report](.github/ISSUE_TEMPLATE/bug_report.yml) or a
[feature request](.github/ISSUE_TEMPLATE/feature_request.yml). The forms ask for
the version, the exact command, and the guild's time zone because those are the
fields that otherwise have to be requested in a comment before triage can start —
and a surprising number of scheduling reports turn out to be daylight saving
transitions.

**Report security vulnerabilities privately** through a GitHub security advisory,
never as a public issue. A token compromise or a permission bypass affects every
running instance from the moment it is described in public.

## Licence

Contributions are licensed under the MIT licence, the same as the project. See
[LICENSE](LICENSE). By opening a pull request you confirm you have the right to
contribute the code under those terms.
