using Fushi.Core.Identifiers;

namespace Fushi.Host.Logging;

/// <summary>
/// Log messages emitted by the host's own background work.
/// </summary>
/// <remarks>
/// Event identifiers 4000 to 4099 belong to the host. The application layer owns
/// 1000 upwards, the gateway 2000, and the Discord surface 3000, so a dashboard
/// can select any one of them without matching on message text.
/// <br/>
/// The scheduler logs a pass at debug and a transition at information. That split
/// is what makes the information level readable: a pass happens every thirty
/// seconds and says nothing, while a cycle opening happens three times a week and
/// is the narrative somebody actually wants.
/// </remarks>
internal static partial class HostLog
{
    [LoggerMessage(
        EventId = 4000,
        Level = LogLevel.Debug,
        Message = "Scheduler pass over {GuildCount} operational guild(s)")]
    public static partial void SchedulerPass(ILogger logger, int guildCount);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Opened cycle {Code} for guild {GuildId}")]
    public static partial void CycleOpened(ILogger logger, ShortCode code, ulong guildId);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Closed the cycle for guild {GuildId}")]
    public static partial void CycleClosed(ILogger logger, ulong guildId);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Information,
        Message = "Finalised cycle {Code} for guild {GuildId}: {Approved} approved, "
            + "{Rejected} rejected, {Skipped} skipped")]
    public static partial void CycleFinalised(
        ILogger logger,
        ShortCode code,
        ulong guildId,
        int approved,
        int rejected,
        int skipped);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Debug,
        Message = "Guild {GuildId} had nothing to {Transition}: {Reason}")]
    public static partial void NothingToDo(
        ILogger logger,
        ulong guildId,
        string transition,
        string reason);

    [LoggerMessage(
        EventId = 4005,
        Level = LogLevel.Warning,
        Message = "Could not {Transition} for guild {GuildId}: {Reason}")]
    public static partial void TransitionFailed(
        ILogger logger,
        string transition,
        ulong guildId,
        string reason);

    [LoggerMessage(
        EventId = 4006,
        Level = LogLevel.Error,
        Message = "A scheduler pass threw and was abandoned; the next pass will retry")]
    public static partial void SchedulerFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Information,
        Message = "Captured {Captured} submission(s) from guild {GuildId}, skipping {Skipped} "
            + "of {Read} message(s) read")]
    public static partial void IntakeSwept(
        ILogger logger,
        int captured,
        ulong guildId,
        int skipped,
        int read);

    [LoggerMessage(
        EventId = 4008,
        Level = LogLevel.Debug,
        Message = "Intake sweep of guild {GuildId} found nothing: {Reason}")]
    public static partial void IntakeQuiet(ILogger logger, ulong guildId, string reason);

    [LoggerMessage(
        EventId = 4009,
        Level = LogLevel.Error,
        Message = "An intake sweep threw and was abandoned; the next sweep will retry")]
    public static partial void IntakeFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4010,
        Level = LogLevel.Warning,
        Message = "Applying {Count} pending migration(s) at startup. This is a development "
            + "convenience; production schemas are applied deliberately")]
    public static partial void ApplyingMigrations(ILogger logger, int count);

    [LoggerMessage(
        EventId = 4011,
        Level = LogLevel.Information,
        Message = "The database schema is up to date")]
    public static partial void SchemaCurrent(ILogger logger);

    [LoggerMessage(
        EventId = 4012,
        Level = LogLevel.Information,
        Message = "Registered {Registered} new guild(s) of {Present} the bot is in")]
    public static partial void GuildsRegistered(ILogger logger, int registered, int present);

    [LoggerMessage(
        EventId = 4013,
        Level = LogLevel.Debug,
        Message = "All {Present} guild(s) the bot is in are already registered")]
    public static partial void GuildsCurrent(ILogger logger, int present);

    [LoggerMessage(
        EventId = 4014,
        Level = LogLevel.Warning,
        Message = "Could not register guilds: {Reason}")]
    public static partial void RegistrationSkipped(ILogger logger, string reason);

    [LoggerMessage(
        EventId = 4015,
        Level = LogLevel.Error,
        Message = "A guild registration pass threw and was abandoned; the next pass will retry")]
    public static partial void RegistrationFaulted(ILogger logger, Exception exception);
}
