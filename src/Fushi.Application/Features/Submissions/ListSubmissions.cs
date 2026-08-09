using FluentValidation;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Results;
using Fushi.Core.Utilities.Paging;

namespace Fushi.Application.Features.Submissions;

/// <summary>
/// Reads a page of a guild's submissions, most recent first.
/// </summary>
/// <remarks>
/// Paging is in the database rather than in the handler. A guild that has been
/// running for a year has thousands of submissions, and fetching all of them to
/// show ten would grow steadily slower in a way that nothing would flag until it
/// timed out.
/// <br/>
/// The page number and size arrive as plain integers and are corrected rather
/// than rejected, because they come from a slash command where <c>page:0</c> is a
/// slip of the finger and answering it with the first page is more useful than
/// answering it with a complaint.
/// </remarks>
/// <param name="GuildId">The guild to list.</param>
/// <param name="Status">
/// The lifecycle state to filter by, or <see langword="null"/> for every state.
/// </param>
/// <param name="PageNumber">
/// The one-based page number, clamped to at least <c>1</c>. Named for the number
/// rather than simply <c>Page</c> so that it cannot shadow
/// <see cref="Page{T}"/> in the response type.
/// </param>
/// <param name="PageSize">
/// How many to return per page, clamped to <see cref="PageRequest.MAX_SIZE"/>.
/// </param>
/// <seealso cref="SubmissionSummaryModel"/>
public sealed record ListSubmissions(
    ulong GuildId,
    SubmissionStatus? Status = null,
    int PageNumber = 1,
    int PageSize = PageRequest.DEFAULT_SIZE) : IQuery<Page<SubmissionSummaryModel>>;

/// <summary>
/// Checks the shape of a <see cref="ListSubmissions"/> query.
/// </summary>
/// <remarks>
/// The page number and size are deliberately not checked, because the handler
/// clamps them and a rule that rejected what the handler would have corrected
/// would make the two disagree. An undefined <see cref="SubmissionStatus"/> is a
/// different matter: it can only come from a caller that has invented a value,
/// and it would silently filter everything out.
/// </remarks>
internal sealed class ListSubmissionsValidator : AbstractValidator<ListSubmissions>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public ListSubmissionsValidator()
    {
        RuleFor(query => query.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(query => query.Status)
            .Must(static status => status is null || Enum.IsDefined(status.Value))
            .WithMessage("That is not a submission state.");
    }
}

/// <summary>
/// Carries out <see cref="ListSubmissions"/>.
/// </summary>
/// <param name="submissions">The submission store.</param>
internal sealed class ListSubmissionsHandler(ISubmissionRepository submissions)
    : IQueryHandler<ListSubmissions, Page<SubmissionSummaryModel>>
{
    /// <inheritdoc/>
    public async Task<Result<Page<SubmissionSummaryModel>>> HandleAsync(
        ListSubmissions request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = PageRequest.Clamp(request.PageNumber, request.PageSize);

        Page<Submission> found = await submissions.ListAsync(
            request.GuildId,
            request.Status,
            page,
            cancellationToken);

        // Mapped rather than re-queried, so the position the repository worked
        // out travels with the projected items instead of being counted twice.
        return found.Map(static submission => new SubmissionSummaryModel(
            submission.Code.ToString(),
            submission.Title,
            submission.ApplicantId,
            submission.Mention,
            submission.Status,
            submission.Outcome,
            submission.CreatedAt));
    }
}

/// <summary>
/// One submission, reduced to a line in a list.
/// </summary>
/// <remarks>
/// No tally. A listing does not load votes, so any count reported here would be
/// zero for every row, and a zero that looks like a count is read as "nobody
/// voted" rather than as "not asked". A reader who wants the tally opens the
/// submission, where it is real.
/// </remarks>
/// <param name="Code">The public code, in its canonical rendering.</param>
/// <param name="Title">The short summary.</param>
/// <param name="ApplicantId">The applying user's snowflake.</param>
/// <param name="ApplicantMention">The applicant as Discord mention markup.</param>
/// <param name="Status">Where the submission sits in its lifecycle.</param>
/// <param name="Outcome">
/// The verdict, or <see langword="null"/> while it has not been judged.
/// </param>
/// <param name="CapturedAt">When it was collected from the intake channel.</param>
public sealed record SubmissionSummaryModel(
    string Code,
    string Title,
    ulong ApplicantId,
    string ApplicantMention,
    SubmissionStatus Status,
    SubmissionOutcome? Outcome,
    DateTimeOffset CapturedAt);
