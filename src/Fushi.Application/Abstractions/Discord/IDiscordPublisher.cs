using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Results;

namespace Fushi.Application.Abstractions.Discord;

/// <summary>
/// Posts and updates the messages through which the bot talks to a guild.
/// </summary>
/// <remarks>
/// Takes entities and returns message snowflakes. It deliberately says nothing
/// about embeds, components, or colours: how a submission looks is a presentation
/// decision belonging to the Discord layer, and a handler that composed an embed
/// would be unable to change its own mind about layout without a rewrite.
/// <br/>
/// Every method returns a <see cref="Result"/> rather than throwing. A Discord
/// call failing is an ordinary event — rate limits, deleted channels, revoked
/// permissions — and a handler needs to record what it managed to do rather than
/// unwind everything because a message could not be edited.
/// </remarks>
public interface IDiscordPublisher
{
    /// <summary>
    /// Posts a submission to a review channel for voting.
    /// </summary>
    /// <param name="channelId">The review channel.</param>
    /// <param name="submission">The submission to publish.</param>
    /// <param name="policy">
    /// The rules in force, so the message can state the bar the submission has
    /// to clear.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>
    /// The snowflake of the posted message, to be recorded on the submission so
    /// it can be edited as votes arrive.
    /// </returns>
    Task<Result<ulong>> PublishSubmissionAsync(
        ulong channelId,
        Submission submission,
        VotingPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites an already-posted submission message to show the current tally.
    /// </summary>
    /// <remarks>
    /// Called after every vote. An implementation should treat a missing message
    /// as a success rather than a failure: if a moderator deleted it, there is
    /// nothing to repair and failing would only block the vote from being
    /// recorded.
    /// </remarks>
    /// <param name="channelId">The review channel.</param>
    /// <param name="messageId">The message to rewrite.</param>
    /// <param name="submission">The submission, with its votes loaded.</param>
    /// <param name="policy">The rules in force.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>Whether the message was brought up to date.</returns>
    Task<Result> RefreshSubmissionAsync(
        ulong channelId,
        ulong messageId,
        Submission submission,
        VotingPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Announces that a cycle has opened.
    /// </summary>
    /// <param name="channelId">The channel to announce in.</param>
    /// <param name="cycle">The cycle that has opened.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The snowflake of the announcement message.</returns>
    Task<Result<ulong>> AnnounceCycleAsync(
        ulong channelId,
        Cycle cycle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes the outcomes of a finished cycle.
    /// </summary>
    /// <param name="channelId">The channel to publish in.</param>
    /// <param name="cycle">
    /// The cycle, with its submissions and their decided outcomes loaded.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The snowflake of the results message.</returns>
    Task<Result<ulong>> PublishResultsAsync(
        ulong channelId,
        Cycle cycle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a decided submission into the archive channel.
    /// </summary>
    /// <param name="channelId">The archive channel.</param>
    /// <param name="submission">The decided submission.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>Whether the submission was archived.</returns>
    Task<Result> ArchiveSubmissionAsync(
        ulong channelId,
        Submission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells an applicant what was decided about their submission.
    /// </summary>
    /// <remarks>
    /// A direct message, which a user can refuse to accept. A closed inbox is
    /// reported as a failure so the caller can note it, but it must not prevent
    /// the outcome from being recorded.
    /// </remarks>
    /// <param name="submission">The decided submission.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>Whether the applicant could be reached.</returns>
    Task<Result> NotifyApplicantAsync(
        Submission submission,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a line to a guild's moderation log channel.
    /// </summary>
    /// <param name="channelId">The log channel.</param>
    /// <param name="message">The line to write.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>Whether the line was written.</returns>
    Task<Result> LogAsync(
        ulong channelId,
        string message,
        CancellationToken cancellationToken = default);
}
