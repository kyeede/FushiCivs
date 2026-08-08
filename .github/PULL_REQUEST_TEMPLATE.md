<!--
Delete any section that does not apply rather than filling it with "n/a".
A short, accurate pull request is easier to review than a complete-looking one.
-->

## What this changes

<!--
The behaviour that is different after this merges, in the terms a user of the
bot would describe it. "Submissions that miss quorum are reported as Skipped
instead of Rejected", not "modified VotingPolicy.Evaluate".
-->

## Why

<!--
The reason the change is worth making. Link the issue if there is one
(Closes #123). If this is a design decision with a plausible alternative, say
what the alternative was and why it lost — that is the part that is expensive
to reconstruct in six months.
-->

## How it works

<!--
Only if the implementation is not obvious from the diff. Note anything a
reviewer would otherwise have to work out: a non-obvious ordering constraint, a
place where the straightforward approach does not work, a deliberate departure
from a pattern used elsewhere.
-->

## Scope

- [ ] Public API changed (new or altered public types or members)
- [ ] Database schema changed (a migration is included)
- [ ] Configuration changed (`.env.example` and `docs/configuration.md` updated)
- [ ] Slash command surface changed (`docs/interactions.md` updated)
- [ ] Domain rules changed (`docs/domain.md` updated)

## Checks

- [ ] `build/build.sh --format` passes locally
- [ ] New behaviour is covered by tests, and the tests fail without the change
- [ ] Public types and members carry XML documentation
- [ ] No new analyzer warnings — they are advisory locally but fatal in CI
- [ ] No secret, token, or real guild identifier appears in the diff

## Migration notes

<!--
Only when the schema changed. State whether the migration is reversible, how
long it takes on a table of realistic size, and whether it needs a lock that
would interrupt a running cycle.
-->

## How this was verified

<!--
What you actually ran, beyond the test suite. "Opened a cycle in a test guild,
cast three votes across two accounts, confirmed the results embed reported 2/3
and Approved." Reviewers cannot exercise a Discord bot from a diff.
-->
