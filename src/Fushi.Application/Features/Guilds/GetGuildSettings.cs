using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence.Repositories;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Results;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Guilds;

/// <summary>
/// Reads everything <c>/config show</c> displays for a guild.
/// </summary>
/// <remarks>
/// No validator accompanies this query. A request carrying nothing but a guild
/// snowflake has nothing worth asserting that the handler does not already have
/// to establish, and an always-passing validator is a file that only ever says
/// yes.
/// </remarks>
/// <param name="GuildId">The guild to read.</param>
/// <seealso cref="GuildSettingsModel"/>
public sealed record GetGuildSettings(ulong GuildId) : IQuery<GuildSettingsModel>;

/// <summary>
/// A guild's complete configuration, arranged for display.
/// </summary>
/// <remarks>
/// Built for one screen rather than mirroring <see cref="Guild"/>. The entity is
/// not returned directly for two reasons: it exposes mutators that a renderer has
/// no business reaching, and it would make the shape of the display depend on the
/// shape of the table, so that a persistence change became a presentation change.
/// <br/>
/// Everything that needs arithmetic has already had it. The approval ratio is
/// carried both as a fraction and as a percentage, and the next opening is an
/// absolute instant rather than a day-and-time the caller would have to resolve
/// against a time zone. A renderer that has to compute is a renderer that can
/// compute differently from the scheduler.
/// </remarks>
/// <param name="GuildId">The guild these settings belong to.</param>
/// <param name="IsEnabled">
/// Whether the guild has switched the bot on. Independent of whether it is
/// configured well enough to do anything.
/// </param>
/// <param name="IsOperational">
/// Whether the guild is both switched on and configured well enough to run a
/// cycle. This is the value that decides whether the scheduler will pick the
/// guild up.
/// </param>
/// <param name="IntakeChannelId">
/// Where applications are collected from, or <see langword="null"/> when unset.
/// </param>
/// <param name="ReviewChannelId">
/// Where submissions are posted for voting, or <see langword="null"/> when unset.
/// </param>
/// <param name="ResultsChannelId">
/// Where outcomes are announced, or <see langword="null"/> when unset.
/// </param>
/// <param name="EffectiveResultsChannelId">
/// Where outcomes will actually be announced, which falls back to the review
/// channel when no results channel was chosen. Resolved here so the display and
/// the publisher cannot disagree about it.
/// </param>
/// <param name="ArchiveChannelId">
/// Where decided submissions are kept, or <see langword="null"/> when archiving
/// is off.
/// </param>
/// <param name="LogChannelId">
/// Where moderation activity is mirrored, or <see langword="null"/> when the
/// trail is kept in the database only.
/// </param>
/// <param name="ApprovalRatio">
/// The share of deciding votes that must be approvals, as a fraction from
/// <c>0</c> to <c>1</c>.
/// </param>
/// <param name="ApprovalPercentage">
/// The same threshold as a whole-number percentage, for display.
/// </param>
/// <param name="Quorum">
/// The deciding votes required for a result to count. Falling short leaves a
/// submission skipped rather than rejected.
/// </param>
/// <param name="AllowAbstain">Whether voters may abstain.</param>
/// <param name="AllowSelfVote">
/// Whether an applicant may vote on their own submission.
/// </param>
/// <param name="AllowVoteChange">
/// Whether a voter may revise their vote before the cycle closes.
/// </param>
/// <param name="Days">The days of the week cycles open on.</param>
/// <param name="OpensAt">The local time voting opens.</param>
/// <param name="ClosesAt">The local time voting closes.</param>
/// <param name="IsOvernight">
/// Whether the window runs past midnight, so a display can say "until 02:00 the
/// next day" rather than showing a closing time that appears to precede the
/// opening one.
/// </param>
/// <param name="TimeZoneId">
/// The IANA identifier the local times are expressed in.
/// </param>
/// <param name="IsTimeZoneKnown">
/// Whether this machine could resolve <paramref name="TimeZoneId"/>. When it is
/// <see langword="false"/> the instants below were computed in UTC instead, and
/// the display should say so rather than presenting them as correct.
/// </param>
/// <param name="VotingGrantCount">
/// How many live voting grants the guild has. A count rather than the grants
/// themselves, because <c>/config show</c> only reports whether anybody can vote;
/// <c>ListVotingPermissions</c> is the query for the detail.
/// </param>
/// <param name="NextOpensAt">
/// The instant voting next starts, or <see langword="null"/> when the schedule
/// runs on no day and therefore never opens.
/// </param>
/// <param name="NextClosesAt">
/// The instant that next window closes, or <see langword="null"/> for the same
/// reason as <paramref name="NextOpensAt"/>.
/// </param>
public sealed record GuildSettingsModel(
    ulong GuildId,
    bool IsEnabled,
    bool IsOperational,
    ulong? IntakeChannelId,
    ulong? ReviewChannelId,
    ulong? ResultsChannelId,
    ulong? EffectiveResultsChannelId,
    ulong? ArchiveChannelId,
    ulong? LogChannelId,
    double ApprovalRatio,
    int ApprovalPercentage,
    int Quorum,
    bool AllowAbstain,
    bool AllowSelfVote,
    bool AllowVoteChange,
    CycleDays Days,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    bool IsOvernight,
    string TimeZoneId,
    bool IsTimeZoneKnown,
    int VotingGrantCount,
    DateTimeOffset? NextOpensAt,
    DateTimeOffset? NextClosesAt);

/// <summary>
/// Carries out <see cref="GetGuildSettings"/>.
/// </summary>
/// <param name="guilds">The guild store.</param>
/// <param name="clock">
/// Supplies the current instant, from which the next opening is measured.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class GetGuildSettingsHandler(
    IGuildRepository guilds,
    TimeProvider clock,
    ILogger<GetGuildSettingsHandler> logger)
    : IQueryHandler<GetGuildSettings, GuildSettingsModel>
{
    /// <summary>
    /// Reads the guild's configuration and resolves the parts of it that depend
    /// on the clock.
    /// </summary>
    /// <remarks>
    /// A missing row is reported as <see cref="GuildErrors.NotFound"/> rather
    /// than created on demand. Every command in this feature tolerates a missing
    /// row by creating one, but a query must not: it runs outside a transaction
    /// the pipeline would commit, and a read that writes is a read nobody can
    /// reason about.
    /// <br/>
    /// The grants are loaded along with the guild because the model reports how
    /// many are live, and an unloaded collection would answer that question with
    /// a confident zero. Loading them costs a join and writes nothing, which is
    /// the property that matters here.
    /// </remarks>
    /// <param name="request">The query to carry out.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The guild's settings, or a failure when it has no row.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    public async Task<Result<GuildSettingsModel>> HandleAsync(
        GetGuildSettings request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guild? guild = await guilds.FindWithPermissionsAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        CycleSchedule schedule = guild.Schedule;
        bool isTimeZoneKnown = schedule.TryResolveTimeZone(out _);

        if (!isTimeZoneKnown)
        {
            // The zone was accepted when it was configured, so this means the
            // machine's time zone database has changed underneath the row. The
            // query still answers, in UTC, because refusing to show a guild its
            // own configuration is a worse outcome than showing it a schedule
            // with a warning attached.
            GuildLog.TimeZoneUnresolved(logger, request.GuildId, schedule.TimeZoneId);

            schedule = new CycleSchedule(
                schedule.Days,
                schedule.OpensAt,
                schedule.ClosesAt,
                TimeZoneInfo.Utc.Id);
        }

        VotingPolicy policy = guild.Policy;
        GuildChannels channels = guild.Channels;
        CycleWindow? next = schedule.NextOpeningAfter(clock.GetUtcNow());

        return new GuildSettingsModel(
            guild.Id,
            guild.IsEnabled,
            guild.IsOperational,
            channels.IntakeChannelId,
            channels.ReviewChannelId,
            channels.ResultsChannelId,
            channels.EffectiveResultsChannelId,
            channels.ArchiveChannelId,
            channels.LogChannelId,
            policy.ApprovalRatio,
            policy.ApprovalPercentage,
            policy.Quorum,
            policy.AllowAbstain,
            policy.AllowSelfVote,
            policy.AllowVoteChange,
            guild.Schedule.Days,
            guild.Schedule.OpensAt,
            guild.Schedule.ClosesAt,
            guild.Schedule.IsOvernight,
            guild.Schedule.TimeZoneId,
            isTimeZoneKnown,
            guild.LiveGrants().Count,
            next?.OpensAt,
            next?.ClosesAt);
    }
}
