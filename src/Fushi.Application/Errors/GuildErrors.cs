using Fushi.Core.Errors;

namespace Fushi.Application.Errors;

/// <summary>
/// Failures that can arise while configuring a guild.
/// </summary>
/// <remarks>
/// Every failure the application can return is declared in one of these
/// catalogues rather than constructed where it is raised. Two reasons, both
/// practical: the set of codes is enumerable, so the Discord layer can be checked
/// for having a message for each; and a code cannot drift, because the string
/// exists once.
/// <br/>
/// Codes read <c>Area.Condition</c> and are part of the contract with the
/// presentation layer. Descriptions are written to be shown to a user unaltered,
/// which is why they explain what to do rather than what went wrong internally.
/// </remarks>
public static class GuildErrors
{
    /// <summary>
    /// The bot has no configuration for the guild.
    /// </summary>
    public static Error NotFound => Error.NotFound(
        "Guild.NotFound",
        "This server has not been set up yet. Run /config channels to get started.");

    /// <summary>
    /// The caller may not change the guild's configuration.
    /// </summary>
    public static Error Forbidden => Error.Forbidden(
        "Guild.Forbidden",
        "You need the Manage Server permission to change these settings.");

    /// <summary>
    /// The bot is switched off for the guild.
    /// </summary>
    public static Error Disabled => Error.Conflict(
        "Guild.Disabled",
        "Fushi is switched off on this server. Run /config enable to turn it back on.");

    /// <summary>
    /// Intake and review channels have not both been set, so no cycle can run.
    /// </summary>
    public static Error NotConfigured => Error.Conflict(
        "Guild.NotConfigured",
        "An intake channel and a review channel must both be set before voting can run. "
        + "Run /config channels.");

    /// <summary>
    /// The same channel was given two roles that cannot be combined.
    /// </summary>
    /// <remarks>
    /// Intake and review must differ. Posting a submission for voting into the
    /// channel it was collected from would let the bot's own post be read back as
    /// a new submission on the next pass.
    /// </remarks>
    public static Error ChannelConflict => Error.Validation(
        "Guild.ChannelConflict",
        "The intake and review channels must be different, or the bot will collect its own "
        + "posts as submissions.");

    /// <summary>
    /// A configured channel does not exist, or the bot cannot see it.
    /// </summary>
    /// <param name="channelId">The channel that could not be used.</param>
    /// <returns>The failure.</returns>
    public static Error ChannelUnreachable(ulong channelId) => Error.Validation(
        "Guild.ChannelUnreachable",
        $"The bot cannot post in <#{channelId}>. Check that the channel exists and that the "
        + "bot has View Channel and Send Messages there.");

    /// <summary>
    /// The time zone identifier given is not one the system recognises.
    /// </summary>
    /// <param name="timeZoneId">The identifier that failed to resolve.</param>
    /// <returns>The failure.</returns>
    public static Error UnknownTimeZone(string timeZoneId) => Error.Validation(
        "Guild.UnknownTimeZone",
        $"'{timeZoneId}' is not a time zone this system knows. Use an IANA name such as "
        + "Europe/Berlin.");
}
