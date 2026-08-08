using Microsoft.Extensions.Logging;

namespace Fushi.Application.Logging;

/// <summary>
/// Log messages emitted while configuring a guild.
/// </summary>
/// <remarks>
/// Event identifiers 1100 to 1199 belong to this feature. See
/// <see cref="PipelineLog"/> for why logging is arranged this way.
/// </remarks>
internal static partial class GuildLog
{
    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message = "Created configuration for guild {GuildId}")]
    public static partial void Created(ILogger logger, ulong guildId);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Information,
        Message = "Guild {GuildId} channels set by {ActorId}: intake {IntakeChannelId}, review {ReviewChannelId}")]
    public static partial void ChannelsConfigured(
        ILogger logger,
        ulong guildId,
        ulong actorId,
        ulong? intakeChannelId,
        ulong? reviewChannelId);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Information,
        Message = "Guild {GuildId} voting policy set by {ActorId} to {ApprovalPercentage}% of at least {Quorum} vote(s)")]
    public static partial void PolicyConfigured(
        ILogger logger,
        ulong guildId,
        ulong actorId,
        int approvalPercentage,
        int quorum);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Information,
        Message = "Guild {GuildId} schedule set by {ActorId} to {Days} {OpensAt}-{ClosesAt} {TimeZoneId}")]
    public static partial void ScheduleConfigured(
        ILogger logger,
        ulong guildId,
        ulong actorId,
        string days,
        TimeOnly opensAt,
        TimeOnly closesAt,
        string timeZoneId);

    [LoggerMessage(
        EventId = 1104,
        Level = LogLevel.Information,
        Message = "Guild {GuildId} switched {State} by {ActorId}")]
    public static partial void EnabledChanged(
        ILogger logger,
        ulong guildId,
        string state,
        ulong actorId);

    [LoggerMessage(
        EventId = 1105,
        Level = LogLevel.Information,
        Message = "Voting rights granted in guild {GuildId} to {Scope} {TargetId} by {ActorId}")]
    public static partial void PermissionGranted(
        ILogger logger,
        ulong guildId,
        string scope,
        ulong targetId,
        ulong actorId);

    [LoggerMessage(
        EventId = 1106,
        Level = LogLevel.Information,
        Message = "Voting rights revoked in guild {GuildId} from {Scope} {TargetId} by {ActorId}")]
    public static partial void PermissionRevoked(
        ILogger logger,
        ulong guildId,
        string scope,
        ulong targetId,
        ulong actorId);

    [LoggerMessage(
        EventId = 1107,
        Level = LogLevel.Warning,
        Message = "Guild {GuildId} has an unusable time zone {TimeZoneId}; falling back to UTC")]
    public static partial void TimeZoneUnresolved(
        ILogger logger,
        ulong guildId,
        string timeZoneId);
}
