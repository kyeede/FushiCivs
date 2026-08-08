using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Logging;

/// <summary>
/// Log messages emitted while running a voting cycle.
/// </summary>
/// <remarks>
/// Event identifiers 1200 to 1299 belong to this feature. See
/// <see cref="PipelineLog"/> for why logging is arranged this way.
/// <br/>
/// The failures in this class are all reported at <see cref="LogLevel.Warning"/>
/// rather than as errors, and none of them stops the operation that raised them.
/// A cycle whose announcement could not be posted has still opened, and the log
/// line is the only place that discrepancy is visible — so it has to be there,
/// and it has to be findable without being alarming.
/// </remarks>
internal static partial class CycleLog
{
    [LoggerMessage(
        EventId = 1200,
        Level = LogLevel.Information,
        Message = "Created cycle {Code} for guild {GuildId}, voting {OpensAt} to {ClosesAt}")]
    public static partial void Created(
        ILogger logger,
        ulong guildId,
        ShortCode code,
        DateTimeOffset opensAt,
        DateTimeOffset closesAt);

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Information,
        Message = "Opened cycle {Code} in guild {GuildId} with {SubmissionCount} submission(s) by {ActorId}")]
    public static partial void Opened(
        ILogger logger,
        ulong guildId,
        ShortCode code,
        int submissionCount,
        ulong actorId);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Information,
        Message = "Closed cycle {Code} in guild {GuildId} by {ActorId}; outcomes not yet applied")]
    public static partial void Closed(
        ILogger logger,
        ulong guildId,
        ShortCode code,
        ulong actorId);

    [LoggerMessage(
        EventId = 1203,
        Level = LogLevel.Information,
        Message = "Finalised cycle {Code} in guild {GuildId}: {Approved} approved, {Rejected} rejected, {Skipped} skipped")]
    public static partial void Finalised(
        ILogger logger,
        ulong guildId,
        ShortCode code,
        int approved,
        int rejected,
        int skipped);

    [LoggerMessage(
        EventId = 1204,
        Level = LogLevel.Information,
        Message = "Cancelled cycle {Code} in guild {GuildId} by {ActorId}, returning {SubmissionCount} submission(s) to the queue: {Reason}")]
    public static partial void Cancelled(
        ILogger logger,
        ulong guildId,
        ShortCode code,
        ulong actorId,
        int submissionCount,
        string reason);

    [LoggerMessage(
        EventId = 1205,
        Level = LogLevel.Debug,
        Message = "Attached submission {SubmissionCode} to cycle {Code} in guild {GuildId}")]
    public static partial void SubmissionAttached(
        ILogger logger,
        ulong guildId,
        ShortCode code,
        ShortCode submissionCode);

    [LoggerMessage(
        EventId = 1206,
        Level = LogLevel.Information,
        Message = "Submission {SubmissionCode} in cycle {Code} was {Outcome} on {Approvals} for, {Rejections} against, {Abstentions} abstained")]
    public static partial void SubmissionDecided(
        ILogger logger,
        ShortCode code,
        ShortCode submissionCode,
        SubmissionOutcome outcome,
        int approvals,
        int rejections,
        int abstentions);

    [LoggerMessage(
        EventId = 1207,
        Level = LogLevel.Warning,
        Message = "Cycle {Code} opened but its announcement could not be posted in {ChannelId}: {ErrorCode}")]
    public static partial void AnnouncementFailed(
        ILogger logger,
        ShortCode code,
        ulong channelId,
        string errorCode);

    [LoggerMessage(
        EventId = 1208,
        Level = LogLevel.Warning,
        Message = "Cycle {Code} was finalised but its results could not be posted in {ChannelId}: {ErrorCode}")]
    public static partial void ResultsPublishFailed(
        ILogger logger,
        ShortCode code,
        ulong channelId,
        string errorCode);

    [LoggerMessage(
        EventId = 1209,
        Level = LogLevel.Warning,
        Message = "Submission {SubmissionCode} in cycle {Code} could not be posted for voting in {ChannelId}: {ErrorCode}")]
    public static partial void SubmissionPublishFailed(
        ILogger logger,
        ShortCode code,
        ShortCode submissionCode,
        ulong channelId,
        string errorCode);

    [LoggerMessage(
        EventId = 1210,
        Level = LogLevel.Warning,
        Message = "Submission {SubmissionCode} was decided but could not be archived in {ChannelId}: {ErrorCode}")]
    public static partial void ArchiveFailed(
        ILogger logger,
        ShortCode submissionCode,
        ulong channelId,
        string errorCode);

    [LoggerMessage(
        EventId = 1211,
        Level = LogLevel.Warning,
        Message = "Applicant {ApplicantId} could not be told the outcome of submission {SubmissionCode}: {ErrorCode}")]
    public static partial void ApplicantNotificationFailed(
        ILogger logger,
        ulong applicantId,
        ShortCode submissionCode,
        string errorCode);

    [LoggerMessage(
        EventId = 1212,
        Level = LogLevel.Debug,
        Message = "No cycle was due as of {AsOf}")]
    public static partial void NothingDue(ILogger logger, DateTimeOffset asOf);

    [LoggerMessage(
        EventId = 1213,
        Level = LogLevel.Information,
        Message = "Guild {GuildId} has nothing queued, so no cycle was opened for {Date}")]
    public static partial void SkippedNothingQueued(
        ILogger logger,
        ulong guildId,
        DateOnly date);
}
