using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Discord;

/// <summary>
/// Reports which guilds the bot is currently a member of.
/// </summary>
/// <remarks>
/// Discord is the authority on this, not the database. The bot can be added to a
/// server or removed from one while the process is stopped, and no event survives
/// to tell it so afterwards — the only reliable way to know is to ask.
/// <br/>
/// Implemented in the Discord-facing layer. This layer declares only what it
/// needs, which is why nothing here mentions a socket client or a REST type.
/// </remarks>
public interface IGuildDirectory
{
    /// <summary>
    /// Lists the guilds the bot is in.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The guild snowflakes, or a failure when Discord cannot currently answer.
    /// The distinction carries weight: an empty list means the bot genuinely
    /// belongs to no server, while a failure means the question went unanswered.
    /// A caller that treated the two alike would read a reconnect as the bot
    /// having been removed from everywhere at once.
    /// </returns>
    Task<Result<IReadOnlyCollection<ulong>>> ListGuildIdsAsync(
        CancellationToken cancellationToken = default);
}
