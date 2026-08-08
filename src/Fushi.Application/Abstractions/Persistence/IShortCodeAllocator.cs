using Fushi.Core.Identifiers;

namespace Fushi.Application.Abstractions.Persistence;

/// <summary>
/// Produces short codes that are not already in use.
/// </summary>
/// <remarks>
/// <see cref="ShortCode.New"/> produces a random code but cannot know whether it
/// is taken. This interface closes that gap: it generates, checks against the
/// guild's existing codes, and retries.
/// <br/>
/// A check-then-insert is still racy on its own, because two commands can both
/// pass the check before either inserts. The unique index in the database is what
/// actually guarantees correctness; this exists so that the overwhelming majority
/// of allocations succeed on the first attempt and the index is a backstop rather
/// than the primary mechanism.
/// </remarks>
public interface IShortCodeAllocator
{
    /// <summary>
    /// Allocates a code not currently used by any submission in the guild.
    /// </summary>
    /// <param name="guildId">The guild the code must be unique within.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>An unused code.</returns>
    /// <exception cref="Fushi.Core.Exceptions.FushiException">
    /// A free code could not be found within the retry budget, which in a space
    /// of over a billion means the guild's codes are pathologically dense and
    /// needs operator attention rather than another retry.
    /// </exception>
    Task<ShortCode> AllocateForSubmissionAsync(
        ulong guildId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates a code not currently used by any cycle in the guild.
    /// </summary>
    /// <param name="guildId">The guild the code must be unique within.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>An unused code.</returns>
    /// <exception cref="Fushi.Core.Exceptions.FushiException">
    /// A free code could not be found within the retry budget.
    /// </exception>
    Task<ShortCode> AllocateForCycleAsync(
        ulong guildId,
        CancellationToken cancellationToken = default);
}
