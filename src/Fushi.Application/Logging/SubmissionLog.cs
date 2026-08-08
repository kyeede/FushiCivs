using Fushi.Core.Entities.Submissions;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Logging;

/// <summary>
/// Log messages emitted while collecting, queueing, and resolving submissions.
/// </summary>
/// <remarks>
/// Event identifiers 1300 to 1399 belong to this feature. See
/// <see cref="PipelineLog"/> for why logging is arranged this way.
/// <br/>
/// Intake is the noisiest thing the bot does, because it runs on a timer whether
/// or not anybody has posted. The sweep boundaries are therefore written at
/// <see cref="LogLevel.Debug"/> and only the summary is written at
/// <see cref="LogLevel.Information"/>, so a quiet guild produces one line an hour
/// rather than one line a message.
/// </remarks>
internal static partial class SubmissionLog
{
    [LoggerMessage(
        EventId = 1300,
        Level = LogLevel.Information,
        Message = "Captured submission {Code} in guild {GuildId} from {ApplicantId}'s message {MessageId}")]
    public static partial void Captured(
        ILogger logger,
        ulong guildId,
        string code,
        ulong applicantId,
        ulong messageId);

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Information,
        Message = "Submission {Code} in guild {GuildId} queued by {ActorId}")]
    public static partial void Queued(ILogger logger, ulong guildId, string code, ulong actorId);

    [LoggerMessage(
        EventId = 1302,
        Level = LogLevel.Information,
        Message = "Submission {Code} in guild {GuildId} withdrawn by {ActorId}: {Reason}")]
    public static partial void Withdrawn(
        ILogger logger,
        ulong guildId,
        string code,
        ulong actorId,
        string? reason);

    [LoggerMessage(
        EventId = 1303,
        Level = LogLevel.Information,
        Message = "Submission {Code} in guild {GuildId} decided {Outcome} by {ActorId}")]
    public static partial void Decided(
        ILogger logger,
        ulong guildId,
        string code,
        SubmissionOutcome outcome,
        ulong actorId);

    [LoggerMessage(
        EventId = 1304,
        Level = LogLevel.Debug,
        Message = "Sweeping intake channel {ChannelId} of guild {GuildId} after message {AfterMessageId}")]
    public static partial void SweepStarted(
        ILogger logger,
        ulong guildId,
        ulong channelId,
        ulong? afterMessageId);

    [LoggerMessage(
        EventId = 1305,
        Level = LogLevel.Information,
        Message = "Intake sweep of guild {GuildId} read {MessagesRead} message(s): {Captured} captured, {Skipped} skipped")]
    public static partial void SweepFinished(
        ILogger logger,
        ulong guildId,
        int messagesRead,
        int captured,
        int skipped);

    [LoggerMessage(
        EventId = 1306,
        Level = LogLevel.Debug,
        Message = "Skipped intake message {MessageId}: {Reason}")]
    public static partial void MessageSkipped(ILogger logger, ulong messageId, string reason);

    [LoggerMessage(
        EventId = 1307,
        Level = LogLevel.Warning,
        Message = "Short code {Code} was allocated twice in one sweep of guild {GuildId}; retrying (attempt {Attempt})")]
    public static partial void CodeCollisionRetried(
        ILogger logger,
        ulong guildId,
        string code,
        int attempt);

    [LoggerMessage(
        EventId = 1308,
        Level = LogLevel.Warning,
        Message = "Intake sweep of guild {GuildId} stopped early: {Reason}")]
    public static partial void SweepHalted(ILogger logger, ulong guildId, string reason);

    [LoggerMessage(
        EventId = 1309,
        Level = LogLevel.Warning,
        Message = "Review message for submission {Code} in guild {GuildId} could not be posted: {ErrorCode}")]
    public static partial void ReviewPublishFailed(
        ILogger logger,
        ulong guildId,
        string code,
        string errorCode);

    [LoggerMessage(
        EventId = 1310,
        Level = LogLevel.Warning,
        Message = "Review message for submission {Code} in guild {GuildId} could not be brought up to date: {ErrorCode}")]
    public static partial void ReviewRefreshFailed(
        ILogger logger,
        ulong guildId,
        string code,
        string errorCode);
}
