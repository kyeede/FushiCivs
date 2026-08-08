using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;
using Fushi.Core.Utilities.Paging;

using FluentValidation;

namespace Fushi.Application.Features.Cycles;

/// <summary>
/// Lists a guild's cycles, most recent first.
/// </summary>
/// <remarks>
/// The history view. Paged rather than capped at some arbitrary recent number,
/// because the question being asked of it is usually "what happened around such and
/// such a date" and that date can be months back.
/// <br/>
/// No guild lookup is performed. A guild with no configuration row has no cycles
/// either, and an empty page is a truthful answer to "show me the history" where a
/// not-found failure would only send the caller to run a configuration command they
/// do not need.
/// </remarks>
/// <param name="GuildId">The guild whose cycles to list.</param>
/// <param name="Paging">
/// The page to return. Build it with <see cref="PageRequest.Clamp"/> when the values
/// came from something a person typed.
/// </param>
public sealed record ListCycles(ulong GuildId, PageRequest Paging)
    : IQuery<Page<CycleSummaryModel>>;

/// <summary>
/// One cycle as it appears in a history listing.
/// </summary>
/// <remarks>
/// The outcome counts are carried on the row rather than left for the caller to
/// total up, so that rendering a page of ten cycles does not mean ten more queries.
/// </remarks>
/// <param name="Code">The cycle's public code.</param>
/// <param name="Date">The local date the cycle was labelled with.</param>
/// <param name="OpensAt">The instant voting opened, or was due to.</param>
/// <param name="ClosesAt">The instant voting closed, or was due to.</param>
/// <param name="Status">Where the cycle reached in its lifecycle.</param>
/// <param name="SubmissionCount">How many submissions the cycle carried.</param>
/// <param name="Approved">How many were approved.</param>
/// <param name="Rejected">How many were rejected.</param>
/// <param name="Skipped">
/// How many never reached quorum. Zero for a cycle that has not been finalised,
/// which is why <paramref name="Status"/> has to be read alongside these counts:
/// three zeroes mean "not decided yet" rather than "nothing happened".
/// </param>
public sealed record CycleSummaryModel(
    ShortCode Code,
    DateOnly Date,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    CycleStatus Status,
    int SubmissionCount,
    int Approved,
    int Rejected,
    int Skipped);

/// <summary>
/// Checks the shape of a <see cref="ListCycles"/> query.
/// </summary>
/// <remarks>
/// Only the guild is asserted. <see cref="PageRequest"/> cannot hold an invalid
/// page: its constructor rejects one, and <see cref="PageRequest.Clamp"/> corrects
/// one. There is nothing left here to check.
/// </remarks>
internal sealed class ListCyclesValidator : AbstractValidator<ListCycles>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public ListCyclesValidator()
    {
        RuleFor(query => query.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");
    }
}

/// <summary>
/// Carries out <see cref="ListCycles"/>.
/// </summary>
/// <param name="cycles">The cycle store.</param>
internal sealed class ListCyclesHandler(ICycleRepository cycles)
    : IQueryHandler<ListCycles, Page<CycleSummaryModel>>
{
    /// <inheritdoc/>
    public async Task<Result<Page<CycleSummaryModel>>> HandleAsync(
        ListCycles request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Page<Cycle> page = await cycles.ListAsync(
            request.GuildId,
            request.Paging,
            cancellationToken);

        return page.Map(Summarise);
    }

    private static CycleSummaryModel Summarise(Cycle cycle)
    {
        int approved = 0;
        int rejected = 0;
        int skipped = 0;

        foreach (Submission submission in cycle.Submissions)
        {
            switch (submission.Outcome)
            {
                case SubmissionOutcome.Approved:
                    approved++;
                    break;
                case SubmissionOutcome.Rejected:
                    rejected++;
                    break;
                case SubmissionOutcome.Skipped:
                    skipped++;
                    break;
                default:
                    break;
            }
        }

        CycleWindow window = cycle.Window;

        return new CycleSummaryModel(
            cycle.Code,
            window.Date,
            window.OpensAt,
            window.ClosesAt,
            cycle.Status,
            cycle.Submissions.Count,
            approved,
            rejected,
            skipped);
    }
}
