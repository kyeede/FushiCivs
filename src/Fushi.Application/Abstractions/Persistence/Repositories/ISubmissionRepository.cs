using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Utilities.Paging;

namespace Fushi.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Reads and stores submissions.
/// </summary>
public interface ISubmissionRepository
{
    /// <summary>
    /// Finds a submission by the code a user typed.
    /// </summary>
    /// <param name="guildId">The guild the request came from.</param>
    /// <param name="code">The public code.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The submission, or <see langword="null"/> when no such code exists here.</returns>
    Task<Submission?> FindByCodeAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a submission by code, with its votes loaded.
    /// </summary>
    /// <remarks>
    /// Required by anything that reads or changes the tally. Without the votes
    /// loaded, <see cref="Submission.Tally"/> would report zeroes and a decision
    /// made from it would be wrong rather than merely incomplete.
    /// </remarks>
    /// <param name="guildId">The guild the request came from.</param>
    /// <param name="code">The public code.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The submission with its votes, or <see langword="null"/>.</returns>
    Task<Submission?> FindWithVotesByCodeAsync(
        ulong guildId,
        ShortCode code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a submission has already been captured from a given
    /// message.
    /// </summary>
    /// <remarks>
    /// Intake re-reads the channel on every pass and after every restart, so
    /// this is what stops one post from becoming several submissions.
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="sourceMessageId">The originating message snowflake.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// <see langword="true"/> when that message has already been captured.
    /// </returns>
    Task<bool> ExistsForMessageAsync(
        ulong guildId,
        ulong sourceMessageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the submissions waiting for a cycle to open.
    /// </summary>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="limit">
    /// The most to return, bounding how many submissions one cycle can carry.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The queued submissions, oldest first.</returns>
    Task<IReadOnlyList<Submission>> ListQueuedAsync(
        ulong guildId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists a guild's submissions, most recent first, optionally filtered by
    /// lifecycle state.
    /// </summary>
    /// <remarks>
    /// The ordering is part of the contract rather than an accident of the query.
    /// Paging over an unordered set is not merely untidy — the database is free to
    /// return rows in a different order on each call, so a row can appear on two
    /// consecutive pages while another is never shown at all.
    /// <br/>
    /// The votes are not loaded. A caller that needs a tally has to read the
    /// submission individually through <see cref="FindWithVotesByCodeAsync"/>,
    /// because <see cref="Submission.Tally"/> counts only what was loaded and would
    /// otherwise report zeroes that read as "nobody voted".
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="status">
    /// The state to filter by, or <see langword="null"/> for every state.
    /// </param>
    /// <param name="request">The page to return.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The requested page of submissions, newest first.</returns>
    Task<Page<Submission>> ListAsync(
        ulong guildId,
        SubmissionStatus? status,
        PageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds submissions whose code or title begins with the given text.
    /// </summary>
    /// <remarks>
    /// Backs the autocomplete on every command that takes a code. Discord allows
    /// a handler roughly three seconds to answer and displays at most 25 choices,
    /// so this must stay a cheap indexed prefix match rather than a general
    /// search.
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="prefix">
    /// What the user has typed so far. An empty value returns the most recent
    /// submissions, which is what an empty autocomplete box should offer.
    /// </param>
    /// <param name="limit">The most to return, at most 25.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The matching submissions, newest first.</returns>
    Task<IReadOnlyList<Submission>> SearchAsync(
        ulong guildId,
        string prefix,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new submission for insertion.
    /// </summary>
    /// <param name="submission">The submission to add.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="submission"/> is <see langword="null"/>.
    /// </exception>
    void Add(Submission submission);
}
