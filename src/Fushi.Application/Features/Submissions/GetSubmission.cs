using System.Globalization;

using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;

using FluentValidation;

namespace Fushi.Application.Features.Submissions;

/// <summary>
/// Reads everything <c>/submission view</c> needs about one submission.
/// </summary>
/// <remarks>
/// The code arrives as text because that is how a person types it, and parsing
/// is the handler's job rather than the caller's. Failing to parse and failing to
/// match are reported as different errors on purpose: a mistyped code needs
/// correcting, whereas a well-formed code that matches nothing means the
/// submission was never here, so telling the user to check their spelling would
/// send them looking for a mistake they did not make.
/// </remarks>
/// <param name="GuildId">The guild the request came from.</param>
/// <param name="Code">The submission's public code, as the user typed it.</param>
/// <seealso cref="SubmissionDetailModel"/>
public sealed record GetSubmission(ulong GuildId, string Code) : IQuery<SubmissionDetailModel>;

/// <summary>
/// Checks the shape of a <see cref="GetSubmission"/> query.
/// </summary>
/// <remarks>
/// Only that something was asked for. Whether the text is a well-formed code is
/// left to the handler, which can answer with
/// <see cref="SubmissionErrors.MalformedCode"/> and its explanation of the
/// alphabet rather than with a generic validation complaint.
/// </remarks>
internal sealed class GetSubmissionValidator : AbstractValidator<GetSubmission>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public GetSubmissionValidator()
    {
        RuleFor(query => query.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(query => query.Code)
            .NotEmpty()
            .WithMessage("A submission code is required.");
    }
}

/// <summary>
/// Carries out <see cref="GetSubmission"/>.
/// </summary>
/// <param name="submissions">The submission store.</param>
/// <param name="cycles">The cycle store, for the cycle a submission is attached to.</param>
/// <param name="guilds">The guild store, for the rules that apply when no cycle does.</param>
internal sealed class GetSubmissionHandler(
    ISubmissionRepository submissions,
    ICycleRepository cycles,
    IGuildRepository guilds)
    : IQueryHandler<GetSubmission, SubmissionDetailModel>
{
    /// <inheritdoc/>
    public async Task<Result<SubmissionDetailModel>> HandleAsync(
        GetSubmission request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ShortCode.TryParse(request.Code, out ShortCode code))
        {
            return SubmissionErrors.MalformedCode(request.Code);
        }

        // With votes, because the tally is the point of the view. Loading the
        // submission alone would report zeroes, and a zero that looks like a
        // count is worse than no count at all.
        Submission? submission = await submissions.FindWithVotesByCodeAsync(
            request.GuildId,
            code,
            cancellationToken);

        if (submission is null)
        {
            return SubmissionErrors.NotFound(code);
        }

        Cycle? cycle = submission.CycleId is { } cycleId
            ? await cycles.FindAsync(cycleId, cancellationToken)
            : null;

        // The cycle's rules where there is one, because a cycle keeps the terms
        // it opened under and the bar shown to a voter must be the bar actually
        // applied. Only a queued submission falls back to the guild's current
        // rules, and those are the ones it will be judged by.
        VotingPolicy policy = cycle?.Policy
            ?? (await guilds.FindAsync(request.GuildId, cancellationToken))?.Policy
            ?? VotingPolicy.Default;

        VoteTally tally = submission.Tally;

        return new SubmissionDetailModel(
            code.ToString(),
            submission.Title,
            submission.Content,
            submission.ApplicantId,
            submission.Mention,
            submission.Status,
            submission.Outcome,
            submission.CreatedAt,
            submission.DecidedAt,
            SourceUrl(submission),
            cycle?.Code.ToString(),
            tally,
            tally.ApprovalPercentage,
            policy.ApprovalPercentage,
            policy.Quorum);
    }

    private static string SourceUrl(Submission submission) => string.Create(
        CultureInfo.InvariantCulture,
        $"https://discord.com/channels/{submission.GuildId}/{submission.SourceChannelId}/{submission.SourceMessageId}");
}

/// <summary>
/// One submission, reduced to what a detail view shows.
/// </summary>
/// <remarks>
/// Built for the view rather than returned as an entity. A projection cannot
/// lazily load anything the renderer forgot to ask for, cannot be mutated by
/// something downstream, and states exactly what the view depends on, so a change
/// to the entity that breaks the view breaks it here where it is visible.
/// </remarks>
/// <param name="Code">The public code, in its canonical rendering.</param>
/// <param name="Title">The short summary.</param>
/// <param name="Content">The body of the application.</param>
/// <param name="ApplicantId">The applying user's snowflake.</param>
/// <param name="ApplicantMention">
/// The applicant as Discord mention markup, so the renderer never builds it and
/// never builds it wrongly.
/// </param>
/// <param name="Status">Where the submission sits in its lifecycle.</param>
/// <param name="Outcome">
/// The verdict, or <see langword="null"/> while it has not been judged.
/// </param>
/// <param name="CapturedAt">When it was collected from the intake channel.</param>
/// <param name="DecidedAt">
/// When the verdict was recorded, or <see langword="null"/> while undecided.
/// </param>
/// <param name="SourceUrl">
/// A link back to the message it was collected from, so a reader can see the
/// original post rather than only the copy.
/// </param>
/// <param name="CycleCode">
/// The code of the cycle judging it, or <see langword="null"/> while it waits in
/// the queue.
/// </param>
/// <param name="Tally">The votes cast so far.</param>
/// <param name="ApprovalPercentage">
/// The share of deciding votes that approved, as a whole number.
/// </param>
/// <param name="RequiredApprovalPercentage">
/// The share it has to reach to pass, as a whole number.
/// </param>
/// <param name="RequiredQuorum">
/// The number of deciding votes needed before the share means anything. Carried
/// alongside the percentage because the bar has two parts, and a submission at
/// 100% of one vote has cleared neither.
/// </param>
public sealed record SubmissionDetailModel(
    string Code,
    string Title,
    string Content,
    ulong ApplicantId,
    string ApplicantMention,
    SubmissionStatus Status,
    SubmissionOutcome? Outcome,
    DateTimeOffset CapturedAt,
    DateTimeOffset? DecidedAt,
    string SourceUrl,
    string? CycleCode,
    VoteTally Tally,
    int ApprovalPercentage,
    int RequiredApprovalPercentage,
    int RequiredQuorum);
