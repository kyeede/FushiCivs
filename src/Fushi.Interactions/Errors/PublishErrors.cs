using System.Globalization;

using Fushi.Core.Errors;

namespace Fushi.Interactions.Errors;

/// <summary>
/// Failures that can arise while writing a message to Discord.
/// </summary>
/// <remarks>
/// Kept apart from the gateway's own catalogue, and not shared with it, because
/// these describe a different kind of trouble. The gateway's errors are about
/// questions it could not answer — who holds this role, does this channel exist.
/// These are about messages that could not be written, and the thing a reader
/// needs to be told is which permission is missing and where.
/// <br/>
/// Every one of them is a failure the bot should survive. A results message that
/// cannot be posted must not stop the results from being recorded, so these are
/// returned rather than thrown, and the handler decides how much of the work
/// still stands.
/// </remarks>
public static class PublishErrors
{
    /// <summary>
    /// The bot is not connected, so nothing can be written.
    /// </summary>
    public static Error Unavailable => Error.Unexpected(
        "Publish.Unavailable",
        "The bot is not connected to Discord at the moment, so the message could not be posted.");

    /// <summary>
    /// The channel is gone, or the bot cannot see it.
    /// </summary>
    /// <param name="channelId">The channel that could not be resolved.</param>
    /// <returns>The failure.</returns>
    public static Error ChannelNotFound(ulong channelId) => Error.NotFound(
        "Publish.ChannelNotFound",
        string.Create(
            CultureInfo.InvariantCulture,
            $"Channel <#{channelId}> no longer exists, or the bot cannot see it. Run "
            + $"`/config channels` to point it somewhere else."));

    /// <summary>
    /// The channel exists but the bot may not post in it.
    /// </summary>
    /// <param name="channelId">The channel that refused the message.</param>
    /// <returns>The failure.</returns>
    public static Error ChannelForbidden(ulong channelId) => Error.Forbidden(
        "Publish.ChannelForbidden",
        string.Create(
            CultureInfo.InvariantCulture,
            $"The bot cannot post in <#{channelId}>. Grant it View Channel, Send Messages, and "
            + $"Embed Links there."));

    /// <summary>
    /// The applicant does not accept direct messages from the bot.
    /// </summary>
    /// <remarks>
    /// Reported as forbidden rather than as a fault, because it is a choice the
    /// user made and nothing on the bot's side will change it. The caller is
    /// expected to note it and carry on.
    /// </remarks>
    /// <param name="userId">The applicant who could not be reached.</param>
    /// <returns>The failure.</returns>
    public static Error DirectMessagesClosed(ulong userId) => Error.Forbidden(
        "Publish.DirectMessagesClosed",
        string.Create(
            CultureInfo.InvariantCulture,
            $"<@{userId}> does not accept direct messages from this server, so they could not be "
            + $"told the outcome. The result stands regardless."));

    /// <summary>
    /// Discord refused the write for some other reason.
    /// </summary>
    /// <param name="statusCode">The HTTP status Discord returned.</param>
    /// <returns>The failure.</returns>
    public static Error ApiFailure(int statusCode) => Error.Unexpected(
        "Publish.ApiFailure",
        string.Create(
            CultureInfo.InvariantCulture,
            $"Discord refused the message with status {statusCode}. Try again shortly."));
}
