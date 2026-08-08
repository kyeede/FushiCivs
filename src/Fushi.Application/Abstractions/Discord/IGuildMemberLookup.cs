using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Discord;

/// <summary>
/// Answers questions about a guild member that only Discord can answer.
/// </summary>
/// <remarks>
/// Role membership is Discord's to know, not the bot's. Caching it locally would
/// mean a revoked role kept voting rights until the cache expired, so it is
/// resolved at the moment of the decision instead.
/// <br/>
/// Implemented in the Discord-facing layer. This layer declares only what it
/// needs, which is why nothing here mentions a socket client or a REST type.
/// </remarks>
public interface IGuildMemberLookup
{
    /// <summary>
    /// Resolves the roles a member currently holds.
    /// </summary>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="userId">The member to resolve.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The member's role snowflakes, an empty set when they hold none, or a
    /// failure when the member is not in the guild or Discord could not be
    /// reached. The distinction matters: an empty set means "no roles, therefore
    /// no role-based grant applies", while a failure means the question is
    /// unanswered and the request must not be allowed to proceed as though the
    /// answer were no.
    /// </returns>
    Task<Result<IReadOnlyCollection<ulong>>> GetRoleIdsAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a member may administer the bot's configuration.
    /// </summary>
    /// <remarks>
    /// True for the guild owner and for anyone holding a role with the Manage
    /// Guild permission. Configuration is gated on Discord's own permission model
    /// rather than on a separate list the bot maintains, so that removing
    /// somebody's authority in Discord removes it here too, with nothing to
    /// remember to update.
    /// </remarks>
    /// <param name="guildId">The guild to look in.</param>
    /// <param name="userId">The member to test.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// Whether the member may configure the bot, or a failure when Discord could
    /// not be reached.
    /// </returns>
    Task<Result<bool>> IsAdministratorAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default);
}
