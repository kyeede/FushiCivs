using Fushi.Core.Entities.Guilds;

namespace Fushi.Application.Abstractions.Persistence.Repositories;

/// <summary>
/// Reads and stores guild configuration.
/// </summary>
/// <remarks>
/// Named after the questions the application actually asks rather than exposing
/// a general-purpose query surface. A repository that returned
/// <c>IQueryable</c> would let a handler build a query the persistence layer had
/// no chance to index for, and would make the set of queries in use impossible
/// to enumerate.
/// </remarks>
public interface IGuildRepository
{
    /// <summary>
    /// Finds a guild's configuration.
    /// </summary>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The guild, or <see langword="null"/> when the bot has no record of it.
    /// </returns>
    Task<Guild?> FindAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a guild's configuration together with its voting grants.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="FindAsync"/> so that the common path does not
    /// pay for a join it does not need. Use this one whenever the answer depends
    /// on who may vote.
    /// </remarks>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The guild with its grants loaded, or <see langword="null"/> when the bot
    /// has no record of it.
    /// </returns>
    Task<Guild?> FindWithPermissionsAsync(
        ulong guildId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a guild's configuration, creating a default one if none exists.
    /// </summary>
    /// <remarks>
    /// The bot can be added to a server while it is offline, so the join event
    /// that would have created the row may never have been seen. Every entry
    /// point therefore tolerates a missing row rather than assuming the join was
    /// observed.
    /// </remarks>
    /// <param name="guildId">The Discord guild snowflake.</param>
    /// <param name="now">The current instant, for the creation stamp.</param>
    /// <param name="actorId">
    /// The actor to credit with creation, or <c>0</c> when the bot is acting on
    /// its own.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The existing or newly created guild.</returns>
    Task<Guild> GetOrCreateAsync(
        ulong guildId,
        DateTimeOffset now,
        ulong actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every guild that is switched on and configured well enough to run a
    /// cycle.
    /// </summary>
    /// <remarks>
    /// The scheduler's input. Filtering here rather than in the scheduler keeps
    /// the work proportional to the number of active guilds instead of the
    /// number of rows.
    /// </remarks>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The guilds a cycle could be opened for.</returns>
    Task<IReadOnlyList<Guild>> ListOperationalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new guild for insertion.
    /// </summary>
    /// <param name="guild">The guild to add.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="guild"/> is <see langword="null"/>.
    /// </exception>
    void Add(Guild guild);
}
