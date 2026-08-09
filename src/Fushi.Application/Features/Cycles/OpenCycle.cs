using FluentValidation;
using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;
using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Cycles;

/// <summary>
/// Opens voting for a guild, attaching everything waiting in the queue.
/// </summary>
/// <remarks>
/// Normally issued by the scheduler when a configured window arrives, and
/// occasionally by a moderator who wants a vote outside the usual days. Both
/// paths run the same command so that a manual cycle is indistinguishable from a
/// scheduled one afterwards; only <see cref="BypassSchedule"/> and the recorded
/// actor differ.
/// <br/>
/// Opening is idempotent by date. If a cycle already exists for today it is
/// reused rather than duplicated, so a process that restarts halfway through a
/// scheduler pass does not leave a guild with two cycles for one day.
/// </remarks>
/// <param name="GuildId">The guild to open voting for.</param>
/// <param name="ActorId">
/// The user issuing the command, or <c>0</c> when the scheduler is acting on its
/// own initiative. Zero is meaningful rather than missing: it is what
/// distinguishes an automated cycle from a moderator's in the audit trail.
/// </param>
/// <param name="BypassSchedule">
/// <see langword="true"/> to open now even though today is not one of the guild's
/// configured days. The window then runs from this instant to the schedule's
/// usual closing time, because a manual cycle that claimed to have opened hours
/// ago would accept votes for a period nobody could have voted in.
/// </param>
public sealed record OpenCycle(
    ulong GuildId,
    ulong ActorId,
    bool BypassSchedule = false) : ICommand<ShortCode>;

/// <summary>
/// Checks the shape of an <see cref="OpenCycle"/> command.
/// </summary>
/// <remarks>
/// The actor is deliberately not required to be non-zero. Everything else about
/// opening — whether the day is scheduled, whether anything is queued, whether a
/// cycle is already running — needs the database and belongs to the handler.
/// </remarks>
internal sealed class OpenCycleValidator : AbstractValidator<OpenCycle>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public OpenCycleValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");
    }
}

/// <summary>
/// Carries out <see cref="OpenCycle"/>.
/// </summary>
/// <param name="guilds">The guild store, for the channels, policy, and schedule.</param>
/// <param name="cycles">The cycle store.</param>
/// <param name="submissions">The submission store, for the waiting queue.</param>
/// <param name="shortCodes">Allocates the cycle's public code.</param>
/// <param name="publisher">Posts the submissions and the announcement.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">
/// Supplies the current instant. <see cref="TimeProvider"/> rather than a bespoke
/// clock interface: it is the framework's own abstraction, and tests can
/// substitute a fake without this project defining one.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class OpenCycleHandler(
    IGuildRepository guilds,
    ICycleRepository cycles,
    ISubmissionRepository submissions,
    IShortCodeAllocator shortCodes,
    IDiscordPublisher publisher,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<OpenCycleHandler> logger)
    : ICommandHandler<OpenCycle, ShortCode>
{
    // How many submissions one cycle may carry. A voting message people are
    // expected to read through in an evening has a practical ceiling, and this
    // also bounds the number of Discord calls a single open can make.
    private const int MAX_SUBMISSIONS_PER_CYCLE = 25;

    /// <inheritdoc/>
    public async Task<Result<ShortCode>> HandleAsync(
        OpenCycle request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guild? guild = await guilds.FindAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        if (!guild.IsEnabled)
        {
            return GuildErrors.Disabled;
        }

        if (guild.Channels is not
            {
                IntakeChannelId: not null,
                ReviewChannelId: { } reviewChannelId,
            })
        {
            return GuildErrors.NotConfigured;
        }

        if (await cycles.FindOpenAsync(request.GuildId, cancellationToken) is { } running)
        {
            return CycleErrors.AlreadyOpen(running.Code);
        }

        CycleSchedule schedule = guild.Schedule;
        if (!schedule.TryResolveTimeZone(out TimeZoneInfo? zone))
        {
            // Resolving the window would throw, and a misconfigured zone is
            // something an administrator can correct, so it is reported rather
            // than raised.
            return GuildErrors.UnknownTimeZone(schedule.TimeZoneId);
        }

        DateTimeOffset now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        CycleWindow? scheduled = schedule.WindowFor(today);

        if (!request.BypassSchedule && scheduled is null)
        {
            return CycleErrors.NotACycleDay;
        }

        IReadOnlyList<Submission> queued = await submissions.ListQueuedAsync(
            request.GuildId,
            MAX_SUBMISSIONS_PER_CYCLE,
            cancellationToken);

        if (queued.Count == 0)
        {
            CycleLog.SkippedNothingQueued(logger, request.GuildId, today);
            return CycleErrors.NothingQueued;
        }

        Result<Cycle> resolved = await ResolveCycleAsync(
            request,
            guild,
            schedule,
            zone,
            today,
            scheduled,
            now,
            cancellationToken);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        Cycle cycle = resolved.Value;
        cycle.TransitionTo(CycleStatus.Open, now, request.ActorId);

        foreach (Submission submission in queued)
        {
            cycle.Attach(submission);
            submission.PutUnderReview(cycle.Id, now, request.ActorId);

            Result<ulong> posted = await publisher.PublishSubmissionAsync(
                reviewChannelId,
                submission,
                cycle.Policy,
                cancellationToken);

            if (posted.IsSuccess)
            {
                submission.SetReviewMessage(
                    posted.Value,
                    threadId: null,
                    now,
                    request.ActorId);
            }
            else
            {
                CycleLog.SubmissionPublishFailed(
                    logger,
                    cycle.Code,
                    submission.Code,
                    reviewChannelId,
                    posted.Error.Code);
            }

            audit.Record(AuditEntry.Record(
                request.GuildId,
                AuditScope.Submission,
                AuditAction.SubmissionUnderReview,
                now,
                request.ActorId,
                submission.Id,
                submission.Code));

            CycleLog.SubmissionAttached(logger, request.GuildId, cycle.Code, submission.Code);
        }

        Result<ulong> announced = await publisher.AnnounceCycleAsync(
            reviewChannelId,
            cycle,
            cancellationToken);

        if (announced.IsSuccess)
        {
            cycle.SetAnnouncementMessage(announced.Value, now, request.ActorId);
        }
        else
        {
            // The cycle is open whether or not Discord accepted the
            // announcement, and refusing to record that would leave the
            // database disagreeing with reality: votes would arrive on messages
            // the bot had decided never existed. The publish failure is noted
            // and the command succeeds.
            CycleLog.AnnouncementFailed(
                logger,
                cycle.Code,
                reviewChannelId,
                announced.Error.Code);
        }

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Cycle,
            AuditAction.CycleOpened,
            now,
            request.ActorId,
            cycle.Id,
            cycle.Code,
            metadata: Describe(cycle, queued.Count, request.BypassSchedule)));

        CycleLog.Opened(logger, request.GuildId, cycle.Code, queued.Count, request.ActorId);

        return cycle.Code;
    }

    private async Task<Result<Cycle>> ResolveCycleAsync(
        OpenCycle request,
        Guild guild,
        CycleSchedule schedule,
        TimeZoneInfo zone,
        DateOnly today,
        CycleWindow? scheduled,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        Cycle? existing = await cycles.FindByDateAsync(request.GuildId, today, cancellationToken);

        if (existing is not null)
        {
            // The state is checked here rather than left to Cycle.TransitionTo,
            // which throws on an impossible move. A guild whose cycle was
            // cancelled earlier today is an ordinary situation and the caller
            // deserves a sentence explaining it, not an internal error.
            if (existing.IsTerminal)
            {
                return CycleErrors.Concluded(existing.Code);
            }

            return existing.Status is CycleStatus.Scheduled
                ? existing
                : CycleErrors.InvalidTransition(existing.Status, CycleStatus.Open);
        }

        CycleWindow window = !request.BypassSchedule && scheduled is { } planned
            ? planned
            : ImmediateWindow(now, today, schedule, zone);

        ShortCode code = await shortCodes.AllocateForCycleAsync(
            request.GuildId,
            cancellationToken);

        // The guild's policy is copied into the cycle here and never consulted
        // again. Raising the threshold tomorrow must not change the terms of the
        // vote being opened today.
        var created = new Cycle(
            Guid.CreateVersion7(now),
            code,
            request.GuildId,
            window,
            guild.Policy,
            now,
            request.ActorId);

        cycles.Add(created);
        CycleLog.Created(logger, request.GuildId, code, window.OpensAt, window.ClosesAt);

        return created;
    }

    /// <summary>
    /// Builds the window for a cycle opened by hand.
    /// </summary>
    /// <remarks>
    /// Runs from now until the schedule's usual closing time, rolling to
    /// tomorrow when that time has already gone. Reusing the configured closing
    /// time rather than inventing a duration keeps a manual cycle finishing when
    /// people expect cycles to finish.
    /// </remarks>
    private static CycleWindow ImmediateWindow(
        DateTimeOffset now,
        DateOnly today,
        CycleSchedule schedule,
        TimeZoneInfo zone)
    {
        DateTime localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var localClose = today.ToDateTime(schedule.ClosesAt, DateTimeKind.Unspecified);

        if (localClose <= localNow)
        {
            localClose = localClose.AddDays(1);
        }

        var closes = new DateTimeOffset(localClose, zone.GetUtcOffset(localClose)).ToUniversalTime();

        return new CycleWindow(today, now.ToUniversalTime(), closes);
    }

    private static string Describe(Cycle cycle, int submissionCount, bool bypassed)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            cycle.ScheduledDate,
            cycle.OpensAt,
            cycle.ClosesAt,
            Policy = cycle.Policy.ToString(),
            SubmissionCount = submissionCount,
            ScheduleBypassed = bypassed,
        });
}
