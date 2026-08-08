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

namespace Fushi.Application.Features.Votes;

/// <summary>
/// Takes back the caller's own vote on a submission.
/// </summary>
/// <remarks>
/// Only ever the caller's own vote. Removing somebody else's would be a
/// moderation action on a record of what a person said, and there is no command
/// for it: a vote that should not have counted is dealt with by revoking the
/// grant, which leaves the trail intact.
/// <br/>
/// No voting grant is required to retract. Casting a vote needs permission
/// because it adds influence; taking one back only removes influence, and a voter
/// whose rights were withdrawn between casting and retracting should not be left
/// unable to undo something they are no longer entitled to have done.
/// </remarks>
/// <param name="GuildId">The guild the vote was cast in.</param>
/// <param name="VoterId">The voting user's snowflake.</param>
/// <param name="Code">The submission's public code, as the user typed it.</param>
/// <seealso cref="CastVote"/>
public sealed record RetractVote(ulong GuildId, ulong VoterId, string Code) : ICommand;

/// <summary>
/// Checks the shape of a <see cref="RetractVote"/> command.
/// </summary>
internal sealed class RetractVoteValidator : AbstractValidator<RetractVote>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public RetractVoteValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.VoterId)
            .NotEqual(0uL)
            .WithMessage("A voter is required.");

        RuleFor(command => command.Code)
            .NotEmpty()
            .WithMessage("A submission code is required.");
    }
}

/// <summary>
/// Carries out <see cref="RetractVote"/>.
/// </summary>
/// <param name="guilds">The guild store, for the channel the review message sits in.</param>
/// <param name="submissions">The submission store.</param>
/// <param name="cycles">The cycle store, for the window the retraction must fall in.</param>
/// <param name="publisher">Brings the review message up to date afterwards.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">
/// Supplies the current instant. <see cref="TimeProvider"/> rather than a bespoke
/// clock interface: it is the framework's own abstraction, and tests can
/// substitute a fake without this project defining one.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class RetractVoteHandler(
    IGuildRepository guilds,
    ISubmissionRepository submissions,
    ICycleRepository cycles,
    IDiscordPublisher publisher,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<RetractVoteHandler> logger)
    : ICommandHandler<RetractVote>
{
    /// <inheritdoc/>
    public async Task<Result> HandleAsync(
        RetractVote request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ShortCode.TryParse(request.Code, out ShortCode code))
        {
            return SubmissionErrors.MalformedCode(request.Code);
        }

        string rendered = code.ToString();

        Guild? guild = await guilds.FindAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        if (!guild.IsEnabled)
        {
            return GuildErrors.Disabled;
        }

        Submission? submission = await submissions.FindWithVotesByCodeAsync(
            request.GuildId,
            code,
            cancellationToken);

        if (submission is null)
        {
            return SubmissionErrors.NotFound(code);
        }

        if (submission.Status is not SubmissionStatus.UnderReview
            || submission.CycleId is not { } cycleId)
        {
            return SubmissionErrors.NotUnderReview(code);
        }

        Cycle? cycle = await cycles.FindAsync(cycleId, cancellationToken);
        if (cycle is null)
        {
            return CycleErrors.NoneOpen;
        }

        DateTimeOffset now = clock.GetUtcNow();

        // The same clock-based test as casting, and for the same reason: once
        // the window has passed the tally is what the outcome will be worked out
        // from, and letting a voter pull their vote out after that would change
        // a result that has effectively already been reached.
        if (!cycle.IsAcceptingVotes(now))
        {
            VoteLog.ArrivedLate(
                logger,
                request.GuildId,
                rendered,
                request.VoterId,
                cycle.ClosesAt);

            return VoteErrors.WindowClosed;
        }

        // Found before it is removed, because the audit entry names the vote and
        // there is nothing left to name afterwards.
        Vote? existing = submission.FindVote(request.VoterId);
        if (existing is null)
        {
            return VoteErrors.NotCast(code);
        }

        _ = submission.RetractVote(request.VoterId, now);

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Vote,
            AuditAction.VoteRetracted,
            now,
            request.VoterId,
            subjectId: existing.Id,
            subjectCode: code,
            targetId: request.VoterId));

        await RefreshAsync(guild, submission, cycle.Policy, rendered, cancellationToken);

        VoteLog.Retracted(logger, request.GuildId, rendered, request.VoterId);

        VoteTally tally = submission.Tally;
        VoteLog.TallyChanged(
            logger,
            rendered,
            tally.Approvals,
            tally.Rejections,
            tally.Abstentions,
            tally.ApprovalPercentage);

        return Result.Success();
    }

    // The retraction has already happened by the time this runs, so a Discord
    // failure is recorded and swallowed rather than allowed to unwind it. A
    // stale review message is a cosmetic problem; a vote that the user was told
    // was withdrawn but which still counts is not.
    private async Task RefreshAsync(
        Guild guild,
        Submission submission,
        VotingPolicy policy,
        string code,
        CancellationToken cancellationToken)
    {
        if (guild.Channels.ReviewChannelId is not { } reviewChannelId
            || submission.ReviewMessageId is not { } reviewMessageId)
        {
            return;
        }

        Result refreshed = await publisher.RefreshSubmissionAsync(
            reviewChannelId,
            reviewMessageId,
            submission,
            policy,
            cancellationToken);

        if (refreshed.IsFailure)
        {
            SubmissionLog.ReviewRefreshFailed(logger, guild.Id, code, refreshed.Error.Code);
        }
    }
}
