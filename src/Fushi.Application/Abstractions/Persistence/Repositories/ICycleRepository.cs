using Fushi.Core.Entities.Cycles;
using Fushi.Core.Identifiers;
using Fushi.Core.Utilities.Paging;

namespace Fushi.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Reads and stores voting cycles.
/// </summary>
public interface ICycleRepository
{
    /// <summary>
    /// Finds a cycle by its internal identifier.
    /// </summary>
    /// <param name="id">The cycle identifier.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The cycle, or <see langword="null"/> when it does not exist.</returns>
    Task<Cycle?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a cycle by the code a user typed.
    /// </summary>
    /// <remarks>
    /// Scoped to the guild because codes are only unique within one. Accepting a
    /// code without a guild would let someone in one server address a cycle in
    /// another.
    /// </remarks>
    /// <param name="guildId">The guild the request came from.</param>
    /// <param name="code">The public code.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The cycle, or <see langword="null"/> when no such code exists here.</returns>
    Task<Cycle?> FindByCodeAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the guild's currently open cycle, with its submissions and votes
    /// loaded.
    /// </summary>
    /// <remarks>
    /// At most one cycle per guild is open at a time. The persistence layer
    /// enforces that with a filtered unique index rather than leaving it to the
    /// handler, so two concurrent open commands cannot both succeed.
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The open cycle, or <see langword="null"/> when voting is not in progress.
    /// </returns>
    Task<Cycle?> FindOpenAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the cycle scheduled for a given local date.
    /// </summary>
    /// <remarks>
    /// Used to make cycle creation idempotent: the scheduler asks whether the
    /// day's cycle already exists before creating it, so a restart mid-pass does
    /// not produce two.
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="date">The local date the cycle would be labelled with.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The cycle, or <see langword="null"/> when none exists for that date.</returns>
    Task<Cycle?> FindByDateAsync(
        ulong guildId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a cycle by code, with its submissions and their votes loaded.
    /// </summary>
    /// <remarks>
    /// Required by anything that judges a cycle. <see cref="FindByCodeAsync"/>
    /// leaves the graph unloaded, and <see cref="Cycle.Submissions"/> would then be
    /// empty — so a tally computed from it would report zeroes and every submission
    /// would be skipped for want of a quorum. That is a wrong answer rather than an
    /// incomplete one, which is why it is a separate method instead of an option on
    /// the existing one.
    /// </remarks>
    /// <param name="guildId">The guild the request came from.</param>
    /// <param name="code">The public code.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The cycle with its submissions and votes, or <see langword="null"/> when no
    /// such code exists here.
    /// </returns>
    Task<Cycle?> FindWithSubmissionsAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists closed cycles that have not yet been evaluated.
    /// </summary>
    /// <remarks>
    /// Closing a cycle and deciding its submissions are separate steps, so there is
    /// a real interval in which a cycle sits closed and unjudged. This is how the
    /// scheduler finds those — <see cref="ListDueToCloseAsync"/> cannot, because it
    /// returns cycles that are still open.
    /// <br/>
    /// The submissions and their votes are loaded, since the caller exists to judge
    /// them.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The cycles awaiting finalisation, oldest first.</returns>
    Task<IReadOnlyList<Cycle>> ListDueToFinaliseAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a guild's cycles, most recent first, with their submissions loaded.
    /// </summary>
    /// <remarks>
    /// The submissions come along because a cycle listing is not useful without the
    /// outcome counts, and those are read from each submission's recorded outcome.
    /// Their votes are deliberately left behind: the outcome is a column on the
    /// submission, so loading the votes as well would multiply the rows fetched by
    /// the number of voters to answer a question already settled.
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="request">The page to return.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The requested page of cycles.</returns>
    Task<Page<Cycle>> ListAsync(
        ulong guildId,
        PageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists cycles whose opening instant has passed but which have not opened.
    /// </summary>
    /// <param name="asOf">The instant to compare against.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The cycles the scheduler should open.</returns>
    Task<IReadOnlyList<Cycle>> ListDueToOpenAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists open cycles whose closing instant has passed.
    /// </summary>
    /// <param name="asOf">The instant to compare against.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The cycles the scheduler should close and evaluate.</returns>
    Task<IReadOnlyList<Cycle>> ListDueToCloseAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new cycle for insertion.
    /// </summary>
    /// <param name="cycle">The cycle to add.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="cycle"/> is <see langword="null"/>.
    /// </exception>
    void Add(Cycle cycle);
}
