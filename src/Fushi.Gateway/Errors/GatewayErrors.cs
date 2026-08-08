using System.Globalization;

using Fushi.Core.Errors;

namespace Fushi.Gateway.Errors;

/// <summary>
/// Failures that can arise while talking to Discord.
/// </summary>
/// <remarks>
/// Declared in one catalogue rather than constructed where they are raised, for
/// the same two reasons the application layer does it: the set of codes is
/// enumerable, so the presentation layer can be checked for having a message for
/// each, and a code cannot drift because the string exists once.
/// <br/>
/// The division that matters here is between a definite negative answer and an
/// unanswered question. <see cref="Error.NotFound(string, string)"/> and
/// <see cref="Error.Forbidden(string, string)"/> are answers: Discord replied,
/// and the reply was no. <see cref="Error.Unexpected(string, string)"/> means the
/// question never got an answer at all. A caller deciding whether somebody may
/// vote has to treat those differently, so they are never collapsed into one
/// code.
/// </remarks>
public static class GatewayErrors
{
    /// <summary>
    /// The bot is not connected to Discord, so nothing can be resolved.
    /// </summary>
    /// <remarks>
    /// Deliberately <see cref="ErrorType.Unexpected"/> rather than
    /// <see cref="ErrorType.NotFound"/>. During a reconnect the socket cache is
    /// empty and every lookup would otherwise report that the guild does not
    /// exist, which would read as a confident no when the truth is that nobody
    /// asked.
    /// </remarks>
    public static Error Unavailable => Error.Unexpected(
        "Discord.Unavailable",
        "The bot is not connected to Discord at the moment. Try again shortly.");

    /// <summary>
    /// Discord answered, and the answer was a failure.
    /// </summary>
    /// <param name="statusCode">The HTTP status Discord returned.</param>
    /// <returns>The failure.</returns>
    public static Error ApiFailure(int statusCode) => Error.Unexpected(
        "Discord.ApiFailure",
        string.Create(
            CultureInfo.InvariantCulture,
            $"Discord refused the request with status {statusCode}. Try again shortly."));

    /// <summary>
    /// The bot is not in the guild, or Discord no longer knows of it.
    /// </summary>
    /// <param name="guildId">The guild that could not be resolved.</param>
    /// <returns>The failure.</returns>
    public static Error GuildNotFound(ulong guildId) => Error.NotFound(
        "Discord.GuildNotFound",
        string.Create(
            CultureInfo.InvariantCulture,
            $"The bot is not a member of server {guildId}, so it cannot read anything there."));

    /// <summary>
    /// The user is not a member of the guild.
    /// </summary>
    /// <remarks>
    /// A definite answer rather than a fault: Discord was reached and reported
    /// that the person has left or was never there. Distinguished from
    /// <see cref="Unavailable"/> precisely so a caller can act on it, because
    /// treating an outage as "not a member" would quietly strip legitimate voters
    /// of their rights for as long as the outage lasted.
    /// </remarks>
    /// <param name="guildId">The guild that was searched.</param>
    /// <param name="userId">The member who was not found.</param>
    /// <returns>The failure.</returns>
    public static Error MemberNotFound(ulong guildId, ulong userId) => Error.NotFound(
        "Discord.MemberNotFound",
        string.Create(
            CultureInfo.InvariantCulture,
            $"<@{userId}> is not a member of server {guildId}."));

    /// <summary>
    /// The channel does not exist, or the bot cannot see it.
    /// </summary>
    /// <remarks>
    /// Discord does not distinguish a deleted channel from one the bot has no
    /// View Channel permission on: both come back as a 404, because telling an
    /// unprivileged caller that a channel exists would itself leak something. The
    /// description therefore covers both cases rather than claiming one.
    /// </remarks>
    /// <param name="channelId">The channel that could not be resolved.</param>
    /// <returns>The failure.</returns>
    public static Error ChannelNotFound(ulong channelId) => Error.NotFound(
        "Discord.ChannelNotFound",
        string.Create(
            CultureInfo.InvariantCulture,
            $"Channel <#{channelId}> no longer exists, or the bot cannot see it. Check that it is "
            + $"still there and that the bot has View Channel."));

    /// <summary>
    /// The configured channel is not one messages can be read from.
    /// </summary>
    /// <param name="channelId">The channel that was the wrong kind.</param>
    /// <returns>The failure.</returns>
    public static Error ChannelNotText(ulong channelId) => Error.Validation(
        "Discord.ChannelNotText",
        string.Create(
            CultureInfo.InvariantCulture,
            $"<#{channelId}> is not a text channel, so applications cannot be collected from it. "
            + $"Run /config channels to point intake somewhere else."));

    /// <summary>
    /// The bot may see the channel but may not read its history.
    /// </summary>
    /// <param name="channelId">The channel that was refused.</param>
    /// <returns>The failure.</returns>
    public static Error ChannelForbidden(ulong channelId) => Error.Forbidden(
        "Discord.ChannelForbidden",
        string.Create(
            CultureInfo.InvariantCulture,
            $"The bot is not allowed to read <#{channelId}>. Grant it View Channel and Read "
            + $"Message History there."));
}
