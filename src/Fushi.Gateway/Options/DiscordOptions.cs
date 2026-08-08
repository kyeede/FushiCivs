using System.ComponentModel.DataAnnotations;

namespace Fushi.Gateway.Options;

/// <summary>
/// How the bot connects to Discord.
/// </summary>
/// <remarks>
/// Validated on startup rather than on first use. A missing token should stop the
/// process immediately with a clear message, not surface as a login failure some
/// seconds after the host has reported itself healthy.
/// <br/>
/// <see cref="DevelopmentGuildId"/> exists because of how Discord propagates
/// application commands. A command registered to a single guild is usable the
/// instant the call returns; the same command registered globally can take up to
/// an hour to appear everywhere. Development therefore registers to one guild, so
/// that changing a command and restarting is a few seconds rather than a coffee
/// break, and production registers globally, because a bot in many guilds cannot
/// register per guild without hitting rate limits.
/// </remarks>
public sealed class DiscordOptions
{
    /// <summary>
    /// The configuration section these options bind to.
    /// </summary>
    public const string SECTION = "Discord";

    /// <summary>
    /// Gets or sets the bot token used to authenticate with the gateway.
    /// </summary>
    /// <remarks>
    /// Supplied as <c>Discord__Token</c> in the environment or through
    /// <c>dotnet user-secrets</c>, never from <c>appsettings.json</c>, because a
    /// token in a tracked file is a token that has to be rotated.
    /// <br/>
    /// This value must never be logged, in whole or in part. A Discord token is a
    /// bearer credential with no second factor and no scope: anybody holding it
    /// can act as the bot in every guild it has joined, and Discord will not tell
    /// you that it happened. Nothing in this project writes it to a log, and
    /// nothing added later should either.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the guild that slash commands are registered to during
    /// development.
    /// </summary>
    /// <value>
    /// A guild snowflake, or <see langword="null"/> to register commands globally.
    /// Left unset in production.
    /// </value>
    /// <remarks>
    /// Only ever a development convenience. Guild-scoped commands are visible
    /// immediately, which is what makes iterating on a command bearable; global
    /// commands are the correct shape for a deployed bot and are what an unset
    /// value selects.
    /// </remarks>
    public ulong? DevelopmentGuildId { get; set; }

    /// <summary>
    /// Gets or sets the longest reconnect gap treated as ordinary, in seconds.
    /// </summary>
    /// <remarks>
    /// Not a knob on the retry schedule, which is why it is a ceiling rather than
    /// a delay. Discord.Net owns reconnection and backs off internally, and this
    /// project deliberately does not second-guess it. What this value decides is
    /// how a gap is reported: a client that comes back inside it was a blip and is
    /// logged as routine, while one that takes longer was an outage and is logged
    /// at a level somebody is expected to look at.
    /// </remarks>
    [Range(1, 3600)]
    public int ReconnectBackoffCeilingSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets how many messages intake reads from a channel in one pass.
    /// </summary>
    /// <remarks>
    /// Defaults to 100 because that is Discord's own maximum for a single history
    /// request; asking for more does not fetch more, it just silently returns 100.
    /// Lowering it is occasionally useful in a busy guild where a smaller, more
    /// frequent read spreads the work out, so it is configurable rather than a
    /// constant.
    /// </remarks>
    [Range(1, 100)]
    public int IntakePageSize { get; set; } = 100;
}
