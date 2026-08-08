using Microsoft.Extensions.Logging;

namespace Fushi.Gateway.Logging;

/// <summary>
/// Log messages emitted by the connection to Discord.
/// </summary>
/// <remarks>
/// Event identifiers 2000 to 2099 belong to the gateway. The application layer
/// owns 1000 upwards, so the two never collide and a dashboard can select the
/// connection's own events without matching on message text.
/// <br/>
/// Every call goes through a partial method so the
/// <see cref="LoggerMessageAttribute"/> source generator can turn it into a
/// pre-compiled write with no boxed arguments and no format string parsed at run
/// time. The same generator checks the call site, so a message cannot be logged
/// with the wrong number or type of arguments.
/// <br/>
/// No message here takes the bot token, and none should. See
/// <see cref="Options.DiscordOptions.Token"/> for why.
/// </remarks>
internal static partial class GatewayLog
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Connecting to the Discord gateway")]
    public static partial void Connecting(ILogger logger);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Gateway ready as {BotName} ({BotId}) in {GuildCount} guild(s)")]
    public static partial void Ready(ILogger logger, string botName, ulong botId, int guildCount);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Disconnected from the Discord gateway: {Reason}")]
    public static partial void Disconnected(ILogger logger, string reason, Exception? exception);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Reconnected to the Discord gateway after {OutageSeconds:F1}s")]
    public static partial void Reconnected(ILogger logger, double outageSeconds);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Reconnected to the Discord gateway after {OutageSeconds:F1}s, longer than the "
            + "{CeilingSeconds}s treated as ordinary")]
    public static partial void ReconnectedSlowly(
        ILogger logger,
        double outageSeconds,
        int ceilingSeconds);

    [LoggerMessage(
        EventId = 2005,
        Level = LogLevel.Critical,
        Message = "Could not log in to Discord; the token is missing, revoked, or malformed")]
    public static partial void LoginFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2006,
        Level = LogLevel.Warning,
        Message = "Discord returned {StatusCode} for {Operation}: {Reason}")]
    public static partial void ApiFailed(
        ILogger logger,
        int statusCode,
        string operation,
        string reason,
        Exception exception);

    [LoggerMessage(
        EventId = 2007,
        Level = LogLevel.Information,
        Message = "Read {MessageCount} message(s) from intake channel {ChannelId}")]
    public static partial void IntakeRead(ILogger logger, int messageCount, ulong channelId);

    [LoggerMessage(
        EventId = 2008,
        Level = LogLevel.Warning,
        Message = "Could not read intake channel {ChannelId}: {ErrorCode}")]
    public static partial void IntakeReadFailed(ILogger logger, ulong channelId, string errorCode);

    [LoggerMessage(
        EventId = 2009,
        Level = LogLevel.Warning,
        Message = "Could not resolve member {UserId} in guild {GuildId}: {ErrorCode}")]
    public static partial void MemberLookupFailed(
        ILogger logger,
        ulong guildId,
        ulong userId,
        string errorCode);

    [LoggerMessage(
        EventId = 2010,
        Message = "Discord.Net {Source}: {Text}")]
    public static partial void Library(
        ILogger logger,
        LogLevel level,
        string source,
        string text,
        Exception? exception);

    [LoggerMessage(
        EventId = 2011,
        Level = LogLevel.Information,
        Message = "Logged out of Discord and stopped the gateway client")]
    public static partial void ShutDown(ILogger logger);

    [LoggerMessage(
        EventId = 2012,
        Level = LogLevel.Warning,
        Message = "The gateway client did not shut down cleanly; the process is stopping anyway")]
    public static partial void ShutDownFailed(ILogger logger, Exception exception);
}
