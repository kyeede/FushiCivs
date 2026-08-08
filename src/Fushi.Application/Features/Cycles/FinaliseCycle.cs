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

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Cycles;

/// <summary>
/// Applies the outcome to every submission in a closed cycle and publishes the
/// results.
/// </summary>
/// <remarks>
/// The expensive half of finishing a cycle. <see cref="CloseCycle"/> has already
/// guaranteed that no further vote can be counted, so this command is free to take
/// as long as it needs and to be retried when Discord refuses part of it.
/// <br/>
/// Every submission is judged against the policy the cycle copied when it was
/// created, never against the guild's current one. That is the entire purpose of
/// the snapshot: a moderator who raises the pass threshold while a vote is running
/// would otherwise retroactively change what the people who already voted were
/// voting for.
/// </remarks>
/// <param name="GuildId">The guild the cycle belongs to.</param>
/// <param name="Code">The cycle to finalise.</param>
/// <param name="ActorId">
/// The user issuing the command, or <c>0</c> when the scheduler finalises the
/// cycle on its own.
/// </param>
/// <seealso cref="CloseCycle"/>
public sealed record FinaliseCycle(ulong GuildId, ShortCode Code, ulong ActorId)
    : ICommand<CycleResultsModel>;

/// <summary>
/// What a finalised cycle decided, in totals.
/// </summary>
/// <remarks>
/// Counts rather than a list of submissions. The caller is confirming an
/// administrative action, and "12 submissions: 7 approved, 3 rejected, 2 skipped"
/// is the whole of what a moderator needs to see; the detail is already in the
/// results message the cycle published.
/// </remarks>
/// <param name="Code">The cycle that was finalised.</param>
/// <param name="SubmissionCount">
/// How many submissions were judged by this command. Excludes any that had already
/// been decided or withdrawn before it ran, which is why it can be lower than the
/// number the cycle carries.
/// </param>
/// <param name="Approved">How many met both quorum and the approval threshold.</param>
/// <param name="Rejected">
/// How many met quorum but missed the approval threshold.
/// </param>
/// <param name="Skipped">
/// How many never reached quorum. Deliberately not counted as rejections: nobody
/// judged them, so nothing was decided against them.
/// </param>
public sealed record CycleResultsModel(
    ShortCode Code,
    int SubmissionCount,
    int Approved,
    int Rejected,
    int Skipped);

/// <summary>
/// Checks the shape of a <see cref="FinaliseCycle"/> command.
/// </summary>
internal sealed class FinaliseCycleValidator : AbstractValidator<FinaliseCycle>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public FinaliseCycleValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.Code)
            .Must(code => !code.IsEmpty)
            .WithMessage("A cycle code is required.");
    }
}

/// <summary>
/// Carries out <see cref="FinaliseCycle"/>.
/// </summary>
/// <param name="guilds">The guild store, for the results and archive channels.</param>
/// <param name="cycles">The cycle store.</param>
/// <param name="publisher">Posts the results, archives, and notifies applicants.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">Supplies the current instant.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class FinaliseCycleHandler(
    IGuildRepository guilds,
    ICycleRepository cycles,
    IDiscordPublisher publisher,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<FinaliseCycleHandler> logger)
    : ICommandHandler<FinaliseCycle, CycleResultsModel>
{
    /// <inheritdoc/>
    public async Task<Result<CycleResultsModel>> HandleAsync(
        FinaliseCycle request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guild? guild = await guilds.FindAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        // The variant that loads the submissions and their votes. Judging an
        // unloaded collection would read as a unanimous absence of votes and skip
        // every submission in the cycle.
        Cycle? cycle = await cycles.FindWithSubmissionsAsync(
            request.GuildId,
            request.Code,
            cancellationToken);

        if (cycle is null)
        {
            return CycleErrors.NotFound(request.Code);
        }

        // Checked before touching Cycle.TransitionTo, which throws rather than
        // returning a failure. A moderator finalising a cycle twice has made an
        // ordinary mistake and should be told so in a sentence.
        if (cycle.IsTerminal)
        {
            return CycleErrors.Concluded(cycle.Code);
        }

        if (cycle.Status is not CycleStatus.Closed)
        {
            return CycleErrors.InvalidTransition(cycle.Status, CycleStatus.Finalised);
        }

        DateTimeOffset now = clock.GetUtcNow();
        int approved = 0;
        int rejected = 0;
        int skipped = 0;

        // Copied before iterating: the loop decides submissions, and the
        // persistence layer is free to react to that by touching the collection
        // it came from.
        Submission[] carried = [.. cycle.Submissions];

        foreach (Submission submission in carried)
        {
            if (submission.Status is not SubmissionStatus.UnderReview)
            {
                continue;
            }

            VoteTally tally = submission.Tally;
            SubmissionOutcome outcome = cycle.Policy.Evaluate(tally);

            submission.Decide(outcome, now, request.ActorId);

            switch (outcome)
            {
                case SubmissionOutcome.Approved:
                    approved++;
                    break;
                case SubmissionOutcome.Rejected:
                    rejected++;
                    break;
                case SubmissionOutcome.Skipped:
                default:
                    skipped++;
                    break;
            }

            audit.Record(AuditEntry.Record(
                request.GuildId,
                AuditScope.Submission,
                ActionFor(outcome),
                now,
                request.ActorId,
                submission.Id,
                submission.Code,
                targetId: submission.ApplicantId,
                metadata: Describe(tally, cycle.Policy)));

            CycleLog.SubmissionDecided(
                logger,
                cycle.Code,
                submission.Code,
                outcome,
                tally.Approvals,
                tally.Rejections,
                tally.Abstentions);

            await AnnounceOutcomeAsync(guild.Channels, submission, outcome, cancellationToken);
        }

        if (guild.Channels.EffectiveResultsChannelId is { } resultsChannelId)
        {
            Result<ulong> posted = await publisher.PublishResultsAsync(
                resultsChannelId,
                cycle,
                cancellationToken);

            if (posted.IsSuccess)
            {
                cycle.SetResultsMessage(posted.Value, now, request.ActorId);
            }
            else
            {
                // The outcomes have been decided and recorded regardless. A
                // results message that failed to post can be posted again; a
                // decision that was rolled back because of it would leave the
                // submissions under review in a cycle that had already ended.
                CycleLog.ResultsPublishFailed(
                    logger,
                    cycle.Code,
                    resultsChannelId,
                    posted.Error.Code);
            }
        }

        cycle.TransitionTo(CycleStatus.Finalised, now, request.ActorId);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Cycle,
            AuditAction.CycleFinalised,
            now,
            request.ActorId,
            cycle.Id,
            cycle.Code,
            metadata: System.Text.Json.JsonSerializer.Serialize(new
            {
                Approved = approved,
                Rejected = rejected,
                Skipped = skipped,
            })));

        CycleLog.Finalised(logger, request.GuildId, cycle.Code, approved, rejected, skipped);

        return new CycleResultsModel(
            cycle.Code,
            approved + rejected + skipped,
            approved,
            rejected,
            skipped);
    }

    /// <summary>
    /// Archives an approved submission and tells its applicant the outcome.
    /// </summary>
    /// <remarks>
    /// Both are best-effort. An applicant with direct messages closed cannot be
    /// reached, and that is their choice rather than an error in the vote.
    /// </remarks>
    private async Task AnnounceOutcomeAsync(
        GuildChannels channels,
        Submission submission,
        SubmissionOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome is SubmissionOutcome.Approved
            && channels.ArchiveChannelId is { } archiveChannelId)
        {
            Result archived = await publisher.ArchiveSubmissionAsync(
                archiveChannelId,
                submission,
                cancellationToken);

            if (archived.IsFailure)
            {
                CycleLog.ArchiveFailed(
                    logger,
                    submission.Code,
                    archiveChannelId,
                    archived.Error.Code);
            }
        }

        Result notified = await publisher.NotifyApplicantAsync(submission, cancellationToken);
        if (notified.IsFailure)
        {
            CycleLog.ApplicantNotificationFailed(
                logger,
                submission.ApplicantId,
                submission.Code,
                notified.Error.Code);
        }
    }

    private static AuditAction ActionFor(SubmissionOutcome outcome) => outcome switch
    {
        SubmissionOutcome.Approved => AuditAction.SubmissionApproved,
        SubmissionOutcome.Rejected => AuditAction.SubmissionRejected,
        SubmissionOutcome.Skipped => AuditAction.SubmissionSkipped,
        _ => AuditAction.SubmissionSkipped,
    };

    private static string Describe(VoteTally tally, VotingPolicy policy)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            tally.Approvals,
            tally.Rejections,
            tally.Abstentions,
            tally.ApprovalPercentage,
            Policy = policy.ToString(),
        });
}
