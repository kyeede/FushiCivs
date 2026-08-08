using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
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
/// Records a voter's decision on a submission, or changes the one they already
/// made.
/// </summary>
/// <remarks>
/// Casting and changing are one command rather than two. From the voter's side
/// there is only ever one question — how do I stand on this — and which of the
/// two is happening depends on state they cannot see. Splitting them would mean
/// a user could pick the wrong one and be told so.
/// <br/>
/// <see cref="Submission.RecordVote"/> guarantees that a voter never ends up with
/// two live votes, and nothing else. Whether they may vote at all, whether the
/// cycle is still open, whether abstaining is permitted, whether the applicant
/// may vote on their own submission, and whether a vote may be changed are all
/// decided here, because every one of them needs the guild's policy or the
/// caller's roles and the submission has access to neither.
/// </remarks>
/// <param name="GuildId">The guild the vote is being cast in.</param>
/// <param name="VoterId">The voting user's snowflake.</param>
/// <param name="Code">The submission's public code, as the user typed it.</param>
/// <param name="Choice">The decision.</param>
/// <param name="Comment">
/// A justification to attach, or <see langword="null"/> for none.
/// </param>
/// <seealso cref="RetractVote"/>
public sealed record CastVote(
    ulong GuildId,
    ulong VoterId,
    string Code,
    VoteChoice Choice,
    string? Comment = null) : ICommand<VoteReceiptModel>;

/// <summary>
/// Checks the shape of a <see cref="CastVote"/> command.
/// </summary>
/// <remarks>
/// Whether abstaining is allowed is a policy question and belongs to the handler.
/// That the choice is a defined value at all is not: an undefined one can only
/// come from a caller that invented it, and it would otherwise throw out of
/// <see cref="Vote"/> after the permission checks had already been paid for.
/// </remarks>
internal sealed class CastVoteValidator : AbstractValidator<CastVote>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public CastVoteValidator()
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

        RuleFor(command => command.Choice)
            .IsInEnum()
            .WithMessage("Vote for, against, or abstain.");

        RuleFor(command => command.Comment)
            .MaximumLength(Vote.MAX_COMMENT_LENGTH)
            .WithMessage($"A comment can be at most {Vote.MAX_COMMENT_LENGTH} characters.");
    }
}

/// <summary>
/// Carries out <see cref="CastVote"/>.
/// </summary>
/// <param name="guilds">The guild store, read with its voting grants.</param>
/// <param name="submissions">The submission store.</param>
/// <param name="cycles">The cycle store, for the window the vote must fall in.</param>
/// <param name="members">Resolves the voter's roles at the moment of the vote.</param>
/// <param name="publisher">Brings the review message up to date afterwards.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">
/// Supplies the current instant. <see cref="TimeProvider"/> rather than a bespoke
/// clock interface: it is the framework's own abstraction, and tests can
/// substitute a fake without this project defining one.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class CastVoteHandler(
    IGuildRepository guilds,
    ISubmissionRepository submissions,
    ICycleRepository cycles,
    IGuildMemberLookup members,
    IDiscordPublisher publisher,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<CastVoteHandler> logger)
    : ICommandHandler<CastVote, VoteReceiptModel>
{
    /// <inheritdoc/>
    public async Task<Result<VoteReceiptModel>> HandleAsync(
        CastVote request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ShortCode.TryParse(request.Code, out ShortCode code))
        {
            return SubmissionErrors.MalformedCode(request.Code);
        }

        string rendered = code.ToString();

        Guild? guild = await guilds.FindWithPermissionsAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        if (!guild.IsEnabled)
        {
            return GuildErrors.Disabled;
        }

        Result<IReadOnlyCollection<ulong>> roles = await members.GetRoleIdsAsync(
            request.GuildId,
            request.VoterId,
            cancellationToken);

        // An unanswered question must not become a denial. Roles are resolved
        // from Discord on every vote so that a revoked role takes effect at
        // once, and the price of that is that Discord can fail to answer.
        // Reporting the failure as "you may not vote" would tell a legitimate
        // voter something untrue, and they would have no way to tell it apart
        // from a grant they never had.
        if (roles.IsFailure)
        {
            VoteLog.Refused(
                logger,
                request.GuildId,
                rendered,
                request.VoterId,
                "their roles could not be resolved");

            return PermissionErrors.Undetermined;
        }

        if (!guild.CanVote(request.VoterId, roles.Value))
        {
            VoteLog.Refused(
                logger,
                request.GuildId,
                rendered,
                request.VoterId,
                "no grant covers them");

            return PermissionErrors.CannotVote;
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

        // Judged on the clock, not on the stored status. A cycle's closing
        // instant passes before the scheduler gets round to noticing it, and a
        // vote arriving in that gap is late even though the row still says the
        // cycle is open.
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

        // The cycle's policy rather than the guild's current one, because a
        // cycle keeps the terms it opened under and a voter must be held to the
        // rules that were in force when they were asked to vote.
        VotingPolicy policy = cycle.Policy;

        if (request.Choice is VoteChoice.Abstain && !policy.AllowAbstain)
        {
            return VoteErrors.AbstentionNotAllowed;
        }

        if (submission.ApplicantId == request.VoterId && !policy.AllowSelfVote)
        {
            return VoteErrors.SelfVoteNotAllowed;
        }

        Vote? existing = submission.FindVote(request.VoterId);
        if (existing is not null)
        {
            if (!policy.AllowVoteChange)
            {
                return VoteErrors.AlreadyCast(code);
            }

            if (existing.Choice == request.Choice
                && string.Equals(existing.Comment, Normalise(request.Comment), StringComparison.Ordinal))
            {
                return VoteErrors.Unchanged;
            }
        }

        Vote vote = submission.RecordVote(request.VoterId, request.Choice, now, request.Comment);
        bool revised = existing is not null;

        audit.Record(AuditEntry.Record(
            request.GuildId,
            AuditScope.Vote,
            revised ? AuditAction.VoteRevised : AuditAction.VoteCast,
            now,
            request.VoterId,
            subjectId: vote.Id,
            subjectCode: code,
            targetId: request.VoterId,
            reason: vote.Comment,
            metadata: Describe(request.Choice)));

        await RefreshAsync(guild, submission, policy, rendered, cancellationToken);

        if (revised)
        {
            VoteLog.Revised(
                logger,
                request.GuildId,
                rendered,
                request.VoterId,
                request.Choice,
                vote.RevisionCount);
        }
        else
        {
            VoteLog.Cast(logger, request.GuildId, rendered, request.VoterId, request.Choice);
        }

        VoteTally tally = submission.Tally;
        VoteLog.TallyChanged(
            logger,
            rendered,
            tally.Approvals,
            tally.Rejections,
            tally.Abstentions,
            tally.ApprovalPercentage);

        return new VoteReceiptModel(
            rendered,
            request.Choice,
            revised,
            tally,
            tally.ApprovalPercentage,
            policy.ApprovalsNeeded(tally));
    }

    // The vote is already decided by the time this runs. A rate limit or a
    // deleted message must not undo it, so a failure is noted and the command
    // still reports success: the tally in the database is the record, and the
    // message is a rendering of it that the next vote will rewrite anyway.
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

    // Matches the normalisation Vote applies to a comment, so that "no change"
    // here means the same thing it would mean to the entity.
    private static string? Normalise(string? comment)
        => string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

    private static string Describe(VoteChoice choice)
        => System.Text.Json.JsonSerializer.Serialize(new { choice });
}

/// <summary>
/// What a vote did to a submission's standing.
/// </summary>
/// <remarks>
/// Returned so the confirmation a voter sees can show the effect of what they
/// just did without a second round trip, and so it reports the tally that was
/// actually recorded rather than one read back a moment later.
/// </remarks>
/// <param name="Code">The submission's public code, in its canonical rendering.</param>
/// <param name="Choice">The decision now recorded.</param>
/// <param name="WasRevision">
/// <see langword="true"/> when this replaced a vote the caller had already cast.
/// </param>
/// <param name="Tally">The votes standing after the change.</param>
/// <param name="ApprovalPercentage">
/// The share of deciding votes that approved, as a whole number.
/// </param>
/// <param name="ApprovalsNeeded">
/// How many further approvals would carry the submission, assuming no further
/// rejections arrive. <c>0</c> once it would already pass.
/// </param>
public sealed record VoteReceiptModel(
    string Code,
    VoteChoice Choice,
    bool WasRevision,
    VoteTally Tally,
    int ApprovalPercentage,
    int ApprovalsNeeded);
