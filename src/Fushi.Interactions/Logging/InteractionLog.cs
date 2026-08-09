using Microsoft.Extensions.Logging;

namespace Fushi.Interactions.Logging;

/// <summary>
/// Log messages emitted by the Discord surface.
/// </summary>
/// <remarks>
/// Event identifiers 3000 to 3099 belong to this project. The application layer
/// owns 1000 upwards and the gateway 2000 upwards, so a dashboard can select any
/// one of the three without matching on message text.
/// <br/>
/// Nothing here logs the contents of a submission or a vote comment. Those are
/// written by people who did not consent to having them copied into an operator's
/// log, and the identifiers are enough to find the row when something needs
/// investigating.
/// </remarks>
internal static partial class InteractionLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Information,
        Message = "Registered {CommandCount} command(s) to guild {GuildId}")]
    public static partial void RegisteredToGuild(ILogger logger, int commandCount, ulong guildId);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Information,
        Message = "Registered {CommandCount} command(s) globally")]
    public static partial void RegisteredGlobally(ILogger logger, int commandCount);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Error,
        Message = "Could not register commands")]
    public static partial void RegistrationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Warning,
        Message = "Interaction {InteractionId} of kind {Kind} failed: {Reason}")]
    public static partial void InteractionFailed(
        ILogger logger,
        ulong interactionId,
        string kind,
        string reason);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Error,
        Message = "Interaction {InteractionId} of kind {Kind} threw")]
    public static partial void InteractionThrew(
        ILogger logger,
        ulong interactionId,
        string kind,
        Exception exception);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Warning,
        Message = "Could not {Operation} in channel {ChannelId}: {Reason}")]
    public static partial void PublishFailed(
        ILogger logger,
        string operation,
        ulong channelId,
        string reason);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Debug,
        Message = "Review message {MessageId} in channel {ChannelId} is gone; nothing to refresh")]
    public static partial void ReviewMessageGone(ILogger logger, ulong messageId, ulong channelId);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Debug,
        Message = "Could not tell applicant {UserId} the outcome: their inbox is closed")]
    public static partial void ApplicantUnreachable(ILogger logger, ulong userId);
}
