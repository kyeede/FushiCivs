using Fushi.Core.Entities.Submissions;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Logging;

/// <summary>
/// Log messages emitted while casting, revising, and withdrawing votes.
/// </summary>
/// <remarks>
/// Event identifiers 1400 to 1499 belong to this feature. See
/// <see cref="PipelineLog"/> for why logging is arranged this way.
/// <br/>
/// Refusals are recorded at <see cref="LogLevel.Information"/> rather than
/// discarded as ordinary user error. Voting is deny-by-default, so "why could I
/// not vote" is the question a moderator asks most often, and answering it from
/// the log is cheaper than reconstructing the grants as they stood at the time.
/// </remarks>
internal static partial class VoteLog
{
    [LoggerMessage(
        EventId = 1400,
        Level = LogLevel.Information,
        Message = "{VoterId} voted {Choice} on submission {Code} in guild {GuildId}")]
    public static partial void Cast(
        ILogger logger,
        ulong guildId,
        string code,
        ulong voterId,
        VoteChoice choice);

    [LoggerMessage(
        EventId = 1401,
        Level = LogLevel.Information,
        Message = "{VoterId} changed their vote on submission {Code} in guild {GuildId} to {Choice} (revision {RevisionCount})")]
    public static partial void Revised(
        ILogger logger,
        ulong guildId,
        string code,
        ulong voterId,
        VoteChoice choice,
        int revisionCount);

    [LoggerMessage(
        EventId = 1402,
        Level = LogLevel.Information,
        Message = "{VoterId} withdrew their vote on submission {Code} in guild {GuildId}")]
    public static partial void Retracted(
        ILogger logger,
        ulong guildId,
        string code,
        ulong voterId);

    [LoggerMessage(
        EventId = 1403,
        Level = LogLevel.Information,
        Message = "{VoterId} may not vote on submission {Code} in guild {GuildId}: {Reason}")]
    public static partial void Refused(
        ILogger logger,
        ulong guildId,
        string code,
        ulong voterId,
        string reason);

    [LoggerMessage(
        EventId = 1404,
        Level = LogLevel.Information,
        Message = "{VoterId} voted on submission {Code} in guild {GuildId} after voting closed at {ClosedAt:u}")]
    public static partial void ArrivedLate(
        ILogger logger,
        ulong guildId,
        string code,
        ulong voterId,
        DateTimeOffset closedAt);

    [LoggerMessage(
        EventId = 1405,
        Level = LogLevel.Debug,
        Message = "Submission {Code} now stands at {Approvals} for, {Rejections} against, {Abstentions} abstained ({ApprovalPercentage}%)")]
    public static partial void TallyChanged(
        ILogger logger,
        string code,
        int approvals,
        int rejections,
        int abstentions,
        int approvalPercentage);
}
