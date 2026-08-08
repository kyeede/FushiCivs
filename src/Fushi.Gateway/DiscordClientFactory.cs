using Discord;
using Discord.WebSocket;

using Microsoft.Extensions.Logging;

namespace Fushi.Gateway;

/// <summary>
/// Builds the configured <see cref="DiscordSocketClient"/> the whole process
/// shares.
/// </summary>
/// <remarks>
/// Kept out of the registration extension so that the choices below — which
/// intents are asked for, what is cached, how loud the library is — are one
/// readable file rather than a lambda buried in a service collection. They are
/// the decisions most likely to need revisiting, and the ones with the largest
/// consequences if got wrong.
/// </remarks>
public static class DiscordClientFactory
{
    /// <summary>
    /// The gateway intents the bot asks Discord for.
    /// </summary>
    /// <remarks>
    /// Exactly three, and each one is load-bearing.
    /// <br/>
    /// <see cref="GatewayIntents.Guilds"/> populates the guild, channel, and role
    /// caches. Without it the socket client knows of no guilds at all, so every
    /// lookup would fall through to a REST call.
    /// <br/>
    /// <see cref="GatewayIntents.GuildMessages"/> is what makes message history
    /// readable, which is the whole of intake.
    /// <br/>
    /// <see cref="GatewayIntents.MessageContent"/> is a privileged intent: it must
    /// be switched on for the application in the Discord developer portal, and
    /// past a hundred guilds it has to be approved by Discord. Without it every
    /// message arrives with an empty <c>Content</c>, so intake would read the
    /// right messages and find nothing in them. A bot that appears to work but
    /// captures no applications is almost always this.
    /// <br/>
    /// Deliberately absent: <c>GuildMembers</c>, also privileged, which streams
    /// every member of every guild in order to keep a member cache the bot does
    /// not want; and <c>GuildPresences</c>, which is a firehose of status changes
    /// nothing here reads. Roles and membership are resolved on demand instead,
    /// which is both cheaper and, for a decision about who may vote, more correct.
    /// </remarks>
    public const GatewayIntents REQUIRED_INTENTS =
        GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent;

    /// <summary>
    /// How many recent messages per channel the client keeps in memory.
    /// </summary>
    /// <remarks>
    /// Small on purpose. Intake pages through history by snowflake rather than
    /// reading the cache, so the cache exists only to spare a REST call when a
    /// message the bot just posted is edited moments later. A hundred per channel
    /// covers that and nothing more.
    /// </remarks>
    public const int MESSAGE_CACHE_SIZE = 100;

    /// <summary>
    /// Builds the socket configuration.
    /// </summary>
    /// <param name="logger">
    /// The logger the library's own output will be bridged onto, used here only to
    /// discover how verbose the host has been configured to be.
    /// </param>
    /// <returns>The configuration to construct a client from.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public static DiscordSocketConfig CreateConfig(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return new DiscordSocketConfig
        {
            GatewayIntents = REQUIRED_INTENTS,
            MessageCacheSize = MESSAGE_CACHE_SIZE,

            // Downloading every member of every guild is a large amount of memory
            // for data the bot resolves on demand anyway: a guild of fifty
            // thousand people costs fifty thousand cached objects to answer a
            // question asked a few times an hour. Off also means the GuildMembers
            // intent is not needed, which is one fewer privileged intent to
            // justify to Discord.
            AlwaysDownloadUsers = false,

            // Mapped from the host's logging configuration rather than fixed, so
            // that turning the log level up in appsettings turns the library up
            // too. Filtering here rather than in the logger also stops Discord.Net
            // from formatting messages that would be dropped a moment later.
            LogLevel = ToSeverity(logger),

            // Discord accepts a connection that asks for an intent the application
            // has not been granted and then silently sends nothing for it. This
            // turns that into a warning in the log, which is the difference
            // between a five-minute diagnosis and an afternoon.
            LogGatewayIntentWarnings = true,
        };
    }

    /// <summary>
    /// Builds the socket client.
    /// </summary>
    /// <remarks>
    /// Constructed but not connected. Logging in and starting the receive loop
    /// belongs to <see cref="GatewayHostedService"/>, so that the client's
    /// lifetime is tied to the host's rather than to whenever the container first
    /// resolves it.
    /// </remarks>
    /// <param name="logger">The logger to take the verbosity from.</param>
    /// <returns>A configured, disconnected client.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public static DiscordSocketClient Create(ILogger logger) => new(CreateConfig(logger));

    /// <summary>
    /// Translates a Discord.Net severity into the framework's log level.
    /// </summary>
    /// <remarks>
    /// The two scales do not line up. Discord.Net's <c>Verbose</c> is per-message
    /// gateway traffic, which is the framework's <c>Debug</c>, and its <c>Debug</c>
    /// is frame-by-frame detail, which is <c>Trace</c>. Mapping them by name would
    /// put heartbeat frames at a level people leave switched on.
    /// </remarks>
    /// <param name="severity">The severity the library reported.</param>
    /// <returns>The equivalent <see cref="LogLevel"/>.</returns>
    public static LogLevel ToLogLevel(LogSeverity severity) => severity switch
    {
        LogSeverity.Critical => LogLevel.Critical,
        LogSeverity.Error => LogLevel.Error,
        LogSeverity.Warning => LogLevel.Warning,
        LogSeverity.Info => LogLevel.Information,
        LogSeverity.Verbose => LogLevel.Debug,
        LogSeverity.Debug => LogLevel.Trace,
        _ => LogLevel.Information,
    };

    /// <summary>
    /// Discovers the most detailed severity worth asking Discord.Net to produce.
    /// </summary>
    /// <remarks>
    /// Asked of the logger rather than read out of configuration, because the
    /// logger already accounts for every filter rule, provider override, and
    /// category-specific level the host has in force. Reparsing
    /// <c>Logging:LogLevel</c> here would reimplement that badly and drift from it
    /// the first time somebody added a rule.
    /// </remarks>
    /// <param name="logger">The logger the library's output is bridged onto.</param>
    /// <returns>The severity to configure the client with.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="logger"/> is <see langword="null"/>.
    /// </exception>
    public static LogSeverity ToSeverity(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            return LogSeverity.Debug;
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            return LogSeverity.Verbose;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            return LogSeverity.Info;
        }

        return logger.IsEnabled(LogLevel.Warning) ? LogSeverity.Warning : LogSeverity.Error;
    }
}
