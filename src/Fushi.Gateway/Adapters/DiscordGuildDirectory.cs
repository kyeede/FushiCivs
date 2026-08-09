using Fushi.Application.Abstractions.Discord;
using Fushi.Core.Results;
using Fushi.Gateway.Errors;

using Discord;
using Discord.WebSocket;

namespace Fushi.Gateway.Adapters;

/// <summary>
/// Reports the bot's guild membership from the gateway's own view of it.
/// </summary>
/// <remarks>
/// Read from the socket cache rather than fetched over REST. Guild membership
/// arrives on the gateway and Discord keeps it current for the life of the
/// session, so this is reading Discord's live copy rather than a stale one of the
/// bot's own.
/// <br/>
/// The connection check in front of it is the whole point of the class. That cache
/// is empty before the session identifies and is emptied again during a reconnect,
/// so an unguarded read would confidently report zero guilds at exactly the moments
/// the bot knows least. Anything acting on that answer — provisioning, sweeping,
/// scheduling — would conclude the bot had been removed from every server it is in.
/// Refusing to answer is the only safe response to a question that cannot yet be
/// answered.
/// </remarks>
/// <param name="client">The connected socket client.</param>
internal sealed class DiscordGuildDirectory(DiscordSocketClient client) : IGuildDirectory
{
    /// <inheritdoc/>
    public Task<Result<IReadOnlyCollection<ulong>>> ListGuildIdsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (client.ConnectionState != ConnectionState.Connected)
        {
            return Task.FromResult(
                Result<IReadOnlyCollection<ulong>>.Failure(GatewayErrors.Unavailable));
        }

        IReadOnlyCollection<ulong> guildIds = [.. client.Guilds.Select(guild => guild.Id)];

        return Task.FromResult(Result<IReadOnlyCollection<ulong>>.Success(guildIds));
    }
}
