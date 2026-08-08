using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Errors;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;

using FluentValidation;

namespace Fushi.Application.Features.Cycles;

/// <summary>
/// Asks what is happening in a guild right now.
/// </summary>
/// <remarks>
/// The answer to the two questions a member actually has: can I vote at the moment,
/// and if not, when can I? Both are served from one query because a caller cannot
/// know in advance which of them applies, and asking twice would let the answers
/// disagree if a cycle opened between the two reads.
/// </remarks>
/// <param name="GuildId">The guild to report on.</param>
public sealed record GetCycleStatus(ulong GuildId) : IQuery<CycleStatusModel>;

/// <summary>
/// The state of a guild's voting, as either a cycle in progress or the next one
/// due.
/// </summary>
/// <remarks>
/// Exactly one of <see cref="Current"/> and <see cref="NextOpensAt"/> is normally
/// set, and the presentation layer decides what to render from which is present. It
/// is modelled as two nullable members rather than one union so that the empty case
/// — a guild whose schedule has no days at all, where nothing is open and nothing
/// is coming — is representable without inventing a third state to mean "neither".
/// </remarks>
/// <param name="Current">
/// The cycle accepting votes, or <see langword="null"/> when voting is not in
/// progress. When this is set, render it and ignore
/// <see cref="NextOpensAt"/>.
/// </param>
/// <param name="NextOpensAt">
/// When voting next opens, or <see langword="null"/> when the guild's schedule
/// never opens one. Computed from the schedule rather than read from a row, because
/// a future cycle does not exist yet.
/// </param>
public sealed record CycleStatusModel(CurrentCycleModel? Current, DateTimeOffset? NextOpensAt);

/// <summary>
/// The cycle that is accepting votes, and how far through it the guild is.
/// </summary>
/// <param name="Code">The cycle's public code.</param>
/// <param name="Date">The local date the cycle is labelled with.</param>
/// <param name="OpensAt">The instant voting opened.</param>
/// <param name="ClosesAt">The instant voting closes.</param>
/// <param name="Remaining">
/// How long is left before voting closes, or <see cref="TimeSpan.Zero"/> when the
/// window has elapsed but the cycle has not been closed yet. That gap is real: the
/// status lags the clock by however long the scheduler takes to notice.
/// </param>
/// <param name="SubmissionCount">How many submissions the cycle is judging.</param>
/// <param name="VotedCount">
/// How many of those have at least one vote on them. The difference between this
/// and <paramref name="SubmissionCount"/> is what a moderator needs in order to
/// nudge people before the deadline.
/// </param>
/// <param name="Policy">
/// The rules this cycle is being judged under, copied when it opened. Quoted rather
/// than the guild's current policy so that the bar shown to voters is the bar
/// actually applied.
/// </param>
public sealed record CurrentCycleModel(
    ShortCode Code,
    DateOnly Date,
    DateTimeOffset OpensAt,
    DateTimeOffset ClosesAt,
    TimeSpan Remaining,
    int SubmissionCount,
    int VotedCount,
    VotingPolicy Policy);

/// <summary>
/// Checks the shape of a <see cref="GetCycleStatus"/> query.
/// </summary>
internal sealed class GetCycleStatusValidator : AbstractValidator<GetCycleStatus>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public GetCycleStatusValidator()
    {
        RuleFor(query => query.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");
    }
}

/// <summary>
/// Carries out <see cref="GetCycleStatus"/>.
/// </summary>
/// <param name="guilds">The guild store, for the schedule.</param>
/// <param name="cycles">The cycle store.</param>
/// <param name="clock">Supplies the current instant.</param>
internal sealed class GetCycleStatusHandler(
    IGuildRepository guilds,
    ICycleRepository cycles,
    TimeProvider clock)
    : IQueryHandler<GetCycleStatus, CycleStatusModel>
{
    /// <inheritdoc/>
    public async Task<Result<CycleStatusModel>> HandleAsync(
        GetCycleStatus request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guild? guild = await guilds.FindAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        DateTimeOffset now = clock.GetUtcNow();
        Cycle? open = await cycles.FindOpenAsync(request.GuildId, cancellationToken);

        if (open is not null)
        {
            return new CycleStatusModel(Describe(open, now), NextOpensAt: null);
        }

        CycleSchedule schedule = guild.Schedule;
        if (!schedule.TryResolveTimeZone(out _))
        {
            // Every schedule calculation goes through the zone, so an
            // unrecognised one makes the question unanswerable. Reported as a
            // configuration failure rather than answered with "never", which
            // would read as a working schedule that happens to be empty.
            return GuildErrors.UnknownTimeZone(schedule.TimeZoneId);
        }

        return new CycleStatusModel(
            Current: null,
            schedule.NextOpeningAfter(now)?.OpensAt);
    }

    private static CurrentCycleModel Describe(Cycle cycle, DateTimeOffset now)
    {
        int voted = 0;
        foreach (Submission submission in cycle.Submissions)
        {
            if (!submission.Tally.IsEmpty)
            {
                voted++;
            }
        }

        CycleWindow window = cycle.Window;

        return new CurrentCycleModel(
            cycle.Code,
            window.Date,
            window.OpensAt,
            window.ClosesAt,
            window.RemainingFrom(now),
            cycle.Submissions.Count,
            voted,
            cycle.Policy);
    }
}
