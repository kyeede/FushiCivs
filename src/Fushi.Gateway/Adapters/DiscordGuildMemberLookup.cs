using System.Net;

using Fushi.Application.Abstractions.Discord;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Gateway.Errors;
using Fushi.Gateway.Logging;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using Microsoft.Extensions.Logging;

namespace Fushi.Gateway.Adapters;

/// <summary>
/// Answers questions about a guild member by asking Discord.
/// </summary>
/// <remarks>
/// Nothing this class returns is cached, and nothing about it should become
/// cached. Role membership is what decides whether somebody may vote, and a role
/// taken away by a moderator has to stop granting that the moment it is taken
/// away, not when a cache lifetime happens to elapse. A five-minute cache here
/// would mean five minutes in which a removed voter still votes, which is a
/// correctness bug wearing a performance improvement's clothes.
/// <br/>
/// The socket cache is consulted first because the guild, channel, and role data
/// arrives over the gateway and is kept current by Discord itself — reading it is
/// not caching, it is reading Discord's own live copy. Only the member lookup can
/// miss, because <c>AlwaysDownloadUsers</c> is off, and a miss falls through to a
/// REST fetch rather than being answered as an absence.
/// <br/>
/// That last distinction is the one to preserve. A member who has genuinely left
/// the guild is an answer, and is reported as
/// <see cref="ErrorType.NotFound"/>. Discord being unreachable is not an answer,
/// and is reported as <see cref="ErrorType.Unexpected"/>. Collapsing the two would
/// make every outage look like a mass exodus and would silently deny legitimate
/// voters for as long as it lasted — and, worse, would do so without anything in
/// the logs distinguishing it from people simply not being eligible.
/// </remarks>
/// <param name="client">The connected socket client.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class DiscordGuildMemberLookup(
    DiscordSocketClient client,
    ILogger<DiscordGuildMemberLookup> logger)
    : IGuildMemberLookup
{
    /// <inheritdoc/>
    public async Task<Result<IReadOnlyCollection<ulong>>> GetRoleIdsAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ResolveGuild(guildId) is not { } guild)
        {
            return Failed<IReadOnlyCollection<ulong>>(guildId, userId, GuildUnavailable(guildId));
        }

        try
        {
            IGuildUser? member = await ResolveMemberAsync(guild, userId, cancellationToken);
            if (member is null)
            {
                return Failed<IReadOnlyCollection<ulong>>(
                    guildId,
                    userId,
                    GatewayErrors.MemberNotFound(guildId, userId));
            }

            // The @everyone role is included rather than filtered out. Its
            // snowflake is the guild's own, it is genuinely held by the member,
            // and a guild that has granted voting rights to @everyone means it.
            IReadOnlyCollection<ulong> roleIds = [.. member.RoleIds];

            return Result<IReadOnlyCollection<ulong>>.Success(roleIds);
        }
        catch (HttpException exception)
        {
            return Failed<IReadOnlyCollection<ulong>>(
                guildId,
                userId,
                Translate(exception, guildId, userId, nameof(GetRoleIdsAsync)));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A member who is not in the guild is answered with
    /// <see langword="false"/> rather than a failure. That is not the same
    /// judgement as <see cref="GetRoleIdsAsync"/> makes, and the difference is
    /// deliberate: "is this person an administrator here" has a correct answer for
    /// somebody who has left, and it is no. "What roles does this person hold"
    /// does not, and answering it with an empty set would read as a grant of
    /// nothing rather than as a question that could not be asked.
    /// </remarks>
    public async Task<Result<bool>> IsAdministratorAsync(
        ulong guildId,
        ulong userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ResolveGuild(guildId) is not { } guild)
        {
            return Failed<bool>(guildId, userId, GuildUnavailable(guildId));
        }

        // Checked before the member is resolved. The owner always has full
        // authority regardless of what roles they hold, and this saves a REST
        // fetch on the one lookup most likely to happen during first-time setup.
        if (guild.OwnerId == userId)
        {
            return Result<bool>.Success(true);
        }

        try
        {
            IGuildUser? member = await ResolveMemberAsync(guild, userId, cancellationToken);
            if (member is null)
            {
                return Result<bool>.Success(false);
            }

            GuildPermissions permissions = member.GuildPermissions;

            // Administrator is checked as well as Manage Guild because Discord
            // treats it as implying every other permission without setting their
            // bits, so a server owner's admin role would otherwise read as having
            // no authority over the bot's configuration.
            return Result<bool>.Success(permissions.ManageGuild || permissions.Administrator);
        }
        catch (HttpException exception)
        {
            return Failed<bool>(
                guildId,
                userId,
                Translate(exception, guildId, userId, nameof(IsAdministratorAsync)));
        }
    }

    /// <summary>
    /// Finds the guild in the socket cache, which the gateway keeps current.
    /// </summary>
    /// <param name="guildId">The guild to find.</param>
    /// <returns>The guild, or <see langword="null"/> when it is not there.</returns>
    private SocketGuild? ResolveGuild(ulong guildId) => client.GetGuild(guildId);

    /// <summary>
    /// Distinguishes a guild the bot has left from one it simply cannot see yet.
    /// </summary>
    /// <remarks>
    /// The socket cache is empty until the gateway reports ready and is emptied
    /// again during a reconnect, so a missing guild means nothing on its own. The
    /// connection state is what tells the two apart, and getting it wrong here
    /// would turn every reconnect into a wave of "the bot is not in this server"
    /// failures.
    /// </remarks>
    /// <param name="guildId">The guild that could not be resolved.</param>
    /// <returns>The failure to report.</returns>
    private Error GuildUnavailable(ulong guildId) => client.ConnectionState == ConnectionState.Connected
        ? GatewayErrors.GuildNotFound(guildId)
        : GatewayErrors.Unavailable;

    /// <summary>
    /// Resolves a member, falling back to a REST fetch when the socket cache has
    /// never seen them.
    /// </summary>
    /// <remarks>
    /// The fallback is not optional. <c>AlwaysDownloadUsers</c> is off, so the
    /// cache holds only members the bot has already had reason to look at, and
    /// treating a miss as an absence would deny anybody who had not spoken since
    /// the process started.
    /// </remarks>
    /// <param name="guild">The guild to look in.</param>
    /// <param name="userId">The member to resolve.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The member, or <see langword="null"/> when Discord confirms they are not in
    /// the guild.
    /// </returns>
    private static async Task<IGuildUser?> ResolveMemberAsync(
        SocketGuild guild,
        ulong userId,
        CancellationToken cancellationToken)
    {
        if (guild.GetUser(userId) is { } cached)
        {
            return cached;
        }

        RequestOptions options = new()
        {
            CancelToken = cancellationToken,
        };

        return await ((IGuild)guild).GetUserAsync(userId, CacheMode.AllowDownload, options);
    }

    /// <summary>
    /// Turns a Discord HTTP failure into the error that describes it.
    /// </summary>
    /// <remarks>
    /// A 404 from a member fetch is Discord answering the question: the person is
    /// not there. Anything else is Discord declining to answer it, and must not be
    /// reported as an absence.
    /// </remarks>
    /// <param name="exception">The failure Discord returned.</param>
    /// <param name="guildId">The guild that was searched.</param>
    /// <param name="userId">The member that was sought.</param>
    /// <param name="operation">The method that failed, for the log.</param>
    /// <returns>The error to return to the caller.</returns>
    private Error Translate(
        HttpException exception,
        ulong guildId,
        ulong userId,
        string operation)
    {
        GatewayLog.ApiFailed(
            logger,
            (int)exception.HttpCode,
            operation,
            exception.Reason ?? exception.Message,
            exception);

        return exception.HttpCode == HttpStatusCode.NotFound
            ? GatewayErrors.MemberNotFound(guildId, userId)
            : GatewayErrors.ApiFailure((int)exception.HttpCode);
    }

    /// <summary>
    /// Logs a lookup failure and wraps it as a result.
    /// </summary>
    /// <typeparam name="T">The value the caller was asking for.</typeparam>
    /// <param name="guildId">The guild that was searched.</param>
    /// <param name="userId">The member that was sought.</param>
    /// <param name="error">The failure to report.</param>
    /// <returns>A failed result carrying <paramref name="error"/>.</returns>
    private Result<T> Failed<T>(ulong guildId, ulong userId, Error error)
    {
        GatewayLog.MemberLookupFailed(logger, guildId, userId, error.Code);

        return Result<T>.Failure(error);
    }
}
