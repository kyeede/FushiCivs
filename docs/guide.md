# Using Fushi

Fushi collects applications from a channel, puts them in front of a voting panel
on a schedule, and decides them against a threshold you set. This guide covers
every command, every button, and the flow they fit into.

If you want the terse reference — exact option types, permission gates, component
identifiers — see [interactions.md](interactions.md). This page is the one to read
first.

---

## Contents

- [The shape of it](#the-shape-of-it)
- [First-time setup](#first-time-setup)
- [How an application travels](#how-an-application-travels)
- [Command reference](#command-reference)
  - [`/config` — server setup](#config--server-setup)
  - [`/voter` — who may vote](#voter--who-may-vote)
  - [`/cycle` — rounds of voting](#cycle--rounds-of-voting)
  - [`/submission` — looking things up](#submission--looking-things-up)
  - [`/vote` — casting a vote](#vote--casting-a-vote)
- [Buttons, menus, and pop-ups](#buttons-menus-and-pop-ups)
- [Reading a panel](#reading-a-panel)
- [Short codes](#short-codes)
- [When something looks wrong](#when-something-looks-wrong)

---

## The shape of it

Five channels, three roles, one repeating cycle.

| Channel | What it is for | Required | What it can be |
| --- | --- | --- | --- |
| **Intake** | Fushi reads new applications from here | Yes | Text, announcement, thread, or **forum** |
| **Review** | The panel votes here | Yes | Text, announcement, or thread |
| **Results** | Each cycle's outcome is announced here | No | Text, announcement, or thread |
| **Archive** | A copy of every decided application is kept here | No | Text, announcement, or thread |
| **Log** | The audit trail is echoed here as plain text | No | Text, announcement, or thread |

Intake is the only one that takes a **forum**, where each post counts as one
application and the opening message is what gets read. Everywhere else Fushi has
to post and then edit in place, and a forum holds no messages of its own — only
posts — so it cannot be used.

Three separate things decide whether you can do something:

- **A Discord permission.** `/config` needs Manage Server, `/voter` needs Manage
  Roles, `/cycle` needs Manage Messages. Discord hides these commands from anyone
  without the permission, so they will not appear in your list at all.
- **A voting grant.** `/vote` needs one, and it is Fushi's own record — not a
  Discord role, and not implied by any Discord permission. **An administrator
  cannot vote until somebody grants them the right.** This is deliberate: running
  the server and sitting on the panel are different jobs.
- **Ownership.** `/submission withdraw` is for the applicant's own application.

---

## First-time setup

Five steps, in this order. Nothing opens until all of them are done.

**You never type a setting.** No `/config` command takes an option — each one
opens a panel, and every value on that panel is picked from a menu or a button.
Each control saves on its own the moment you use it, so you can set one thing,
wander off, and come back for the rest. There is no form to submit and nothing
half-saved.

### 1. Point Fushi at your channels

```
/config channels
```

You get a list of the five roles, what each is for, and what it currently points
at. Press **Set** or **Change** beside one and you get a picker for just that
role: a channel menu that opens with the current channel already selected, a note
saying which kinds of channel it accepts, and — for the three optional roles — a
**Clear** button.

Choosing a channel takes you straight back to the list, so you can see the change
land and move on to the next one.

Intake and review are the two that matter. The other three are optional and can
be added later. Intake and review cannot be cleared, only pointed somewhere else;
to stop cycles entirely, use `/config disable`.

### 2. Decide what "passing" means

```
/config policy
```

The panel has two menus and three switches.

- **Approval threshold** — the share of **deciding** votes that must approve.
  Offered as whole percentages from 50% to unanimous. Abstentions are not deciding
  votes, so they never drag a result down — they just do not help it.
- **Votes required** — the fewest deciding votes a result may rest on. Below it,
  an application is **skipped** rather than approved or rejected, because too few
  people looked at it for the answer to mean anything.

Defaults are 60% and 3.

The three switches are buttons that flip when pressed. Each one states its current
position in its own label — **Abstentions: allowed** or **Abstentions: off** — so
you never have to work it out from the colour.

| Switch | Default | What it does |
| --- | --- | --- |
| **Abstentions** | allowed | Whether the Abstain button appears at all |
| **Self-votes** | not allowed | Whether applicants may vote on themselves |
| **Changing a vote** | allowed | Whether a voter may change their mind while a cycle is open |

Changing the policy does not disturb a cycle already open. A cycle keeps the rules
it opened under, so nobody's vote is re-judged against a threshold that did not
exist when they cast it.

### 3. Set the schedule

```
/config schedule
```

The days are a multi-select with the current ones already ticked, plus buttons for
the common patterns — **Mon/Wed/Sat**, **Weekdays**, **Weekend**, **Every day** —
and a **Pause** button that stops cycles opening.

Three more buttons lead to the rest:

- **Opening time** and **Closing time** each give you an hour menu and a minute
  menu. Setting one does not disturb the other.
- **Time zone** asks for a region first, then the zone within it, paging where a
  region has more than twenty-five. It opens on the region you are already in.

Times are wall-clock in the zone you pick, so a cycle opens at 10:00 local
whatever daylight saving is doing. Setting a closing time earlier than the opening
time gives you an overnight window, which is supported and is spelled out on the
panel.

### 4. Give people the right to vote

```
/voter grant
```

One menu that takes **users and roles together** — pick up to ten at once and they
are all granted. Granting a role is usually what you want; granting individuals is
there for the person who should vote but should not have the role.

When you grant exactly one, the reply offers **Add a note**, which is worth
writing: it is what `/voter list` shows six months later when nobody remembers why
an account has voting rights.

Nothing can be voted on until at least one grant exists.

### 5. Turn it on

```
/config enable
```

Then check your work:

```
/config show
```

The panel tells you plainly whether cycles can open, and if not, which of the
above is missing.

---

## How an application travels

**Someone posts in the intake channel.** Fushi sweeps that channel every couple of
minutes. The first line of the message becomes the title, the rest becomes the
body, and any attachment links are appended so an image the application depends on
stays reachable after the original scrolls away. Bot posts and empty messages are
skipped. Every capture is recorded, so a message is never picked up twice and a
restart does not duplicate anything.

**The application waits in the queue.** `/submission queue` shows what is waiting.

**A cycle opens** on a scheduled day, at the scheduled time. Everything queued is
pulled into it, posted to the review channel with its voting buttons, and
announced in the results channel.

**The panel votes** — with the buttons on each review message, or with
`/vote cast`. Each vote updates the message it was cast on, so the tally is
current without anybody refreshing anything.

**The cycle closes** at the scheduled time. Voting stops. Nothing is decided yet.

**The cycle is finalised.** Every application in it is judged against the policy
the cycle opened under and comes out **approved**, **rejected**, or **skipped**
(quorum not met). Results are posted publicly, a copy of each application goes to
the archive channel, and each applicant is sent a direct message — which quietly
does nothing if their DMs are closed, rather than failing the decision.

The scheduler does all four of those steps on its own. The `/cycle` commands are
for when you need to intervene.

---

## Command reference

### `/config` — server setup

Needs **Manage Server**. Every reply is private to you. **None of these commands
takes an option** — each opens a panel.

| Command | What it opens |
| --- | --- |
| `/config show` | The whole configuration, with a button into each part of it |
| `/config channels` | The five channel roles, each with its own picker |
| `/config policy` | Approval threshold, quorum, and the three voting switches |
| `/config enable` | Nothing — allows cycles to open straight away |
| `/config disable` | A confirmation. Stops new cycles opening |
| `/config schedule` | Days, opening and closing times, and time zone |

They are five ways into the same set of panels, and every panel has a **Back**
button, so which one you start from only decides how many presses you save.

The reason none of them takes a value is that a slash command option is filled in
blind: Discord shows you the option's name and nothing else — not what the setting
is now, not which values are legal, not what it interacts with. Somebody setting a
closing time had to know the format, the zone it would be read in, and that a time
before the opening time means an overnight window, and found out they had guessed
wrong only when the command was refused. A panel answers all three before you
choose.

**`/config disable`** keeps everything — configuration, applications, grants,
history. It only stops new cycles opening, and a cycle already open is left alone.
`/config enable` starts it again.

### `/voter` — who may vote

Needs **Manage Roles**.

| Command | What it does |
| --- | --- |
| `/voter grant` | A menu of users and roles. Up to ten at once |
| `/voter revoke` | A menu of users and roles. One at a time |
| `/voter list` | Everyone who may vote, with who granted them and why |

`grant` and `revoke` take no options. Both open a menu that offers **users and
roles together**, which is what removed the old rule that you had to fill in
exactly one of two options and would be refused for giving both or neither.

Granting takes up to ten at once, since assembling a panel usually means adding
several people in one go. Granting somebody who already has the right changes
nothing. When you grant exactly one, the reply offers **Add a note**.

Revoking is one at a time, and revoking a **role** asks for confirmation first,
because one grant can be the reason a great many people can vote and the menu
gives no indication of how many. Revoking a single user does not ask. Anyone who
also holds a grant of their own keeps it.

`/voter list` puts a **Revoke** button on every row, which always confirms — a
button on a row is one misplaced press away from its neighbour.

### `/cycle` — rounds of voting

Needs **Manage Messages**. The scheduler does all of this by itself; these are the
manual overrides.

| Command | What it does |
| --- | --- |
| `/cycle status` | The cycle currently open, or when the next one is. **Public** |
| `/cycle open` | Open a cycle now, without waiting. Asks first |
| `/cycle close` | Stop the open cycle accepting votes. Asks first |
| `/cycle finalise` | Decide a closed cycle and publish results. Asks first |
| `/cycle cancel` | Abandon a cycle. Asks first, and asks why |
| `/cycle list` | Recent cycles, newest first |

`/cycle status` is the one command that answers publicly by default. Whether
voting is open is not private, and making people run it themselves to find out
would be friction for nothing.

`/cycle open` takes everything queued into a cycle immediately. The closing time
still comes from the schedule, so opening early gives a longer window rather than
a shifted one.

`/cycle close` stops voting without deciding anything. Finalise afterwards.

`/cycle finalise` takes a `code` and requires the cycle to be closed already.

`/cycle list` puts the one action a cycle still needs on its own row — **Close** on
an open one, **Finalise** on a closed one, **Cancel** on a scheduled one, nothing
on one already decided. Each opens the same confirmation the matching command
does, so the button is a shortcut past looking a code up rather than past being
told what is about to happen.

`/cycle cancel` is the destructive one. Every application goes back to the queue
and **the votes cast under that cycle are deleted**. They were cast in a round
that no longer counts, and carrying them forward would let one person's judgement
apply twice. A pop-up collects the reason for the record. This cannot be undone.

### `/submission` — looking things up

No Discord permission needed.

| Command | What it does |
| --- | --- |
| `/submission view` | One application in full, with its tally |
| `/submission list` | Applications, newest first, optionally filtered |
| `/submission queue` | Just the ones waiting for the next cycle |
| `/submission withdraw` | Withdraw your own application |

`view` and `withdraw` take a `code`, which autocompletes on both the code and the
title — type two characters of either and pick from the list.

`list` takes an optional `status`: **Draft**, **Queued**, **Under review**,
**Decided**, or **Withdrawn**. `queue` is the same list pinned to Queued, given
its own command because "what is waiting" is the question you ask before opening a
cycle, and asking it should not require knowing which status to filter on.

Every row in a list carries a **View** button, so you never have to copy a code
out of one message and into another.

`/submission view` replies privately with a **Post publicly** button, for when
something is worth showing the channel.

Withdrawing opens a pop-up for the reason. It is terminal — a withdrawn
application cannot be put back.

### `/vote` — casting a vote

Needs a **voting grant**, not a Discord permission.

| Command | What it does |
| --- | --- |
| `/vote cast` | Vote on an application |
| `/vote retract` | Remove your vote |

```
/vote cast code:7QK4M2 choice:Approve comment:Strong references
```

`choice` is **Approve**, **Reject**, or **Abstain**. Abstain is refused if the
server has turned it off. `comment` is optional and goes on the record.

You will usually not type this at all — the buttons on the review message do the
same thing in one press.

Voting again replaces your previous vote, if the server allows changes. The tally
counts you once either way.

`/vote retract` needs **no** grant. Somebody whose grant was revoked after they
voted should still be able to take their vote back, and requiring a right they no
longer have would trap it on the record.

---

## Buttons, menus, and pop-ups

Every reply Fushi builds uses Discord's Components V2, so a message is a laid-out
panel with its own controls rather than a coloured box with buttons stuck
underneath.

**On a configuration panel**

| Control | What it does |
| --- | --- |
| **Change** / **Set** | Opens the picker for that one channel role |
| A channel menu | Saves that channel and returns you to the list |
| **Clear** | Unassigns an optional channel. Not offered for intake or review |
| A threshold or quorum menu | Saves immediately and redraws the panel |
| A switch, labelled `allowed` or `off` | Flips that rule to the other position |
| **Opening time** / **Closing time** | An hour menu and a minute menu for that end |
| **Time zone** | A region menu, then the zones within it |
| **Back** | Up one level. Every panel has one |

Three things are true of all of them. A control saves the one setting it names and
leaves the rest alone, so an abandoned panel cannot leave the server half
configured. Every panel is redrawn from what was actually saved rather than from
what you just picked, so what is on screen is what is true — including when
somebody else changed something while you were reading. And nothing is held between
two presses, so a panel still works after Fushi restarts.

**On a review message**

| Control | What it does |
| --- | --- |
| **Approve** / **Reject** | Records your vote and updates the tally in place |
| **Abstain** | Same, if the server allows abstaining |
| **Original** | Jumps to the message in the intake channel |

The vote buttons stay after a decision but go grey, so the message still reads as
something that was voted on. If your server does not allow abstaining, the Abstain
button is not there at all — a control that always refuses is worse than no
control.

**On your vote receipt** (private, only you see it)

| Control | What it does |
| --- | --- |
| **Add a comment** | Opens a pop-up to explain your vote |

The comment attaches to the vote you just cast and does not change the tally. The
button only appears when you did not already give a comment.

**On a list**

| Control | What it does |
| --- | --- |
| **View** / **Revoke** | Acts on that one row |
| **Close** / **Finalise** / **Cancel** | The one thing a cycle on that row still needs |
| **Previous** / **Next** | Moves a page. Greyed out at either end |
| **Dismiss** | Clears the panel |

A row's button acts on that row, which is what removes the step where you read a
short code off one message and typed it into another — the step a code can be
mistyped in.

Paging re-runs the query, so a list you read slowly still shows what is true now,
and a button still works after the bot restarts.

**On a confirmation**

**Confirm** and **Cancel**, with the confirming button in red when the action
cannot be undone.

**Pop-ups** are the one place anything is still typed, and only for prose no menu
could offer: the reason for withdrawing an application, the reason for cancelling
a cycle, a comment on a vote, and a note on a voting grant.

---

## Reading a panel

A stripe runs down the left edge of every panel and tells you the outcome at a
glance — green for approved or open, red for rejected, grey for skipped or idle,
amber for something needing attention.

**On an application:**

- **Status** — where it is: Draft, Queued, Under review, Decided, Withdrawn.
- **Outcome** — how it ended: Approved, Rejected, Skipped. Blank until decided.
- **Tally** — approvals, rejections, and abstentions.
- **Approval** — a bar showing the current share against the one required.
- **Quorum** — the deciding votes cast against the minimum, and how many more
  are needed. Only shown when the server sets a quorum.

**On `/config show`**, the top line says whether cycles can open and, if not,
exactly what is missing. A time zone your host does not recognise gets its own
warning, because while that holds every scheduled time is being computed in UTC —
which is to say, not the times you configured.

---

## Short codes

Applications and cycles are addressed by a six-character code such as `7QK4M2`,
never by a Discord ID.

They are case-insensitive and ignore hyphens, underscores and spaces, so `7qk4m2`,
`7QK-4M2`, and `7QK 4M2` are all the same code.

They also fold the characters people misread off a screenshot: `I` and `L` are
both read as `1`, and `O` is read as `0`. A code that displays as `7QK4M0` is
therefore found just as well by typing `7qk4mo`.

Every option that takes a code autocompletes on both the code and the title, so in
practice nobody types one from memory.

---

## When something looks wrong

**No cycle ever opens.** Run `/config show`. Almost always one of: the server is
disabled, intake or review is unset, or the schedule has no days. The panel names
which.

**Nobody can vote, including admins.** Voting is deny-by-default and independent
of Discord permissions. Check `/voter list` — if it is empty, that is your answer.
Use `/voter grant`.

**Applications are not being picked up.** They must be posted by a person, in the
intake channel, with some text in them. Bot posts and empty messages are skipped
on purpose. Check that Fushi can actually read the channel — `/config show` names
the channel it is watching. If intake is a **forum**, it is the opening message of
each post that is read, and a post archived before Fushi ever saw it will be
missed; that only happens after an outage measured in days.

**Times are wrong by an hour or more.** Check the time zone on `/config show`. If
it is flagged as unrecognised, the schedule is running in UTC. Pick a zone under
`/config schedule` → **Time zone**; every zone offered there is one this host can
resolve, so choosing from the menu cannot reproduce the problem.

**Everything came back Skipped.** Skipped means quorum was not met — fewer
deciding votes than `quorum` requires. Either the panel is smaller than the
setting assumes, or abstentions are doing more work than expected, since they do
not count towards quorum. Lower it on `/config policy`.

**A review message stopped updating its tally.** The vote was still recorded — the
database is the source of truth and a message that cannot be edited never rolls
one back. Usually the bot lost permission to edit in that channel, or the message
was deleted.
