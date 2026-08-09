using System.Net;

using Discord;
using Discord.Net;
using Discord.WebSocket;

using Fushi.Application.Abstractions.Discord;
using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Interactions.Errors;
using Fushi.Interactions.Formatting;
using Fushi.Interactions.Logging;

using Microsoft.Extensions.Logging;

namespace Fushi.Interactions.Publishing;

/// <summary>
/// Writes the bot's messages to Discord.
/// </summary>
/// <remarks>
/// This is the one place where an application-layer instruction — post this
/// submission, announce this cycle — becomes an embed with a colour and a set of
/// buttons. The interface it implements says nothing about any of that, which is
/// what lets the layout change here without a handler being touched.
/// <br/>
/// Nothing throws. Every method catches Discord's HTTP failures and returns them
/// as errors, because a channel being deleted or a permission being revoked is an
/// ordinary Tuesday for a bot, and a vote that was correctly recorded must not be
/// rolled back because the message showing it could not be edited.
/// </remarks>
/// <param name="client">The connected socket client.</param>
/// <param name="logger">The logger to write to.</param>
internal sealed class DiscordPublisher(
    DiscordSocketClient client,
    ILogger<DiscordPublisher> logger)
    : IDiscordPublisher
{
    /// <inheritdoc/>
    public Task<Result<ulong>> PublishSubmissionAsync(
        ulong channelId,
        Submission submission,
        VotingPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return SendAsync(
            channelId,
            SubmissionViews.Review(submission, policy),
            nameof(PublishSubmissionAsync),
            cancellationToken);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A message that is no longer there is reported as success. The only thing
    /// this method exists to do is make an existing message agree with the
    /// database, and a message somebody deleted already agrees with nothing —
    /// there is no repair to attempt and no reason to fail the vote that
    /// triggered the refresh.
    /// </remarks>
    public async Task<Result> RefreshSubmissionAsync(
        ulong channelId,
        ulong messageId,
        Submission submission,
        VotingPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (Resolve(channelId) is not { } channel)
        {
            return Note(nameof(RefreshSubmissionAsync), channelId, Missing(channelId));
        }

        try
        {
            RequestOptions options = Options(cancellationToken);

            if (await channel.GetMessageAsync(messageId, options: options)
                is not IUserMessage message)
            {
                InteractionLog.ReviewMessageGone(logger, messageId, channelId);
                return Result.Success();
            }

            await message.ModifyAsync(
                properties =>
                {
                    properties.Components = SubmissionViews.Review(submission, policy);
                    properties.Flags = MessageFlags.ComponentsV2;

                    // A components-v2 message may carry no embeds, so a review
                    // message posted before the switch has to have its embed
                    // cleared in the same edit or Discord refuses the whole
                    // change and the message stops tracking its own tally.
                    properties.Embeds = Array.Empty<Embed>();
                },
                options);

            return Result.Success();
        }
        catch (HttpException exception) when (exception.HttpCode == HttpStatusCode.NotFound)
        {
            InteractionLog.ReviewMessageGone(logger, messageId, channelId);
            return Result.Success();
        }
        catch (HttpException exception)
        {
            return Note(nameof(RefreshSubmissionAsync), channelId, Translate(exception, channelId));
        }
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> AnnounceCycleAsync(
        ulong channelId,
        Cycle cycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return SendAsync(
            channelId,
            CycleViews.Announcement(cycle),
            nameof(AnnounceCycleAsync),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> PublishResultsAsync(
        ulong channelId,
        Cycle cycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        return SendAsync(
            channelId,
            CycleViews.Results(cycle),
            nameof(PublishResultsAsync),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> ArchiveSubmissionAsync(
        ulong channelId,
        Submission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        Result<ulong> posted = await SendAsync(
            channelId,
            SubmissionViews.Archive(submission),
            nameof(ArchiveSubmissionAsync),
            cancellationToken);

        return posted.IsFailure ? posted.Error : Result.Success();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The user is fetched over REST rather than read from the socket cache. An
    /// applicant whose submission was collected weeks ago may not have spoken
    /// since, and with <c>AlwaysDownloadUsers</c> off the cache would not hold
    /// them — which would turn "has not chatted recently" into "cannot be told
    /// they were accepted".
    /// </remarks>
    public async Task<Result> NotifyApplicantAsync(
        Submission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (client.ConnectionState != ConnectionState.Connected)
        {
            return PublishErrors.Unavailable;
        }

        try
        {
            RequestOptions options = Options(cancellationToken);

            IUser? applicant = await ((IDiscordClient)client).GetUserAsync(
                submission.ApplicantId,
                CacheMode.AllowDownload,
                options);

            if (applicant is null)
            {
                InteractionLog.ApplicantUnreachable(logger, submission.ApplicantId);
                return PublishErrors.DirectMessagesClosed(submission.ApplicantId);
            }

            IDMChannel inbox = await applicant.CreateDMChannelAsync(options);

            await inbox.SendMessageAsync(
                components: SubmissionViews.Archive(submission),
                flags: MessageFlags.ComponentsV2,
                options: options);

            return Result.Success();
        }
        catch (HttpException exception) when (exception.HttpCode == HttpStatusCode.Forbidden)
        {
            InteractionLog.ApplicantUnreachable(logger, submission.ApplicantId);
            return PublishErrors.DirectMessagesClosed(submission.ApplicantId);
        }
        catch (HttpException exception)
        {
            return PublishErrors.ApiFailure((int)exception.HttpCode);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Plain text rather than an embed. This channel is a running record that gets
    /// read by scrolling, and a column of boxes is harder to scan than a column of
    /// lines. Mentions are suppressed so that logging an action taken against
    /// somebody does not ping them every time.
    /// </remarks>
    public async Task<Result> LogAsync(
        ulong channelId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (Resolve(channelId) is not { } channel)
        {
            return Note(nameof(LogAsync), channelId, Missing(channelId));
        }

        try
        {
            await channel.SendMessageAsync(
                message,
                allowedMentions: AllowedMentions.None,
                options: Options(cancellationToken));

            return Result.Success();
        }
        catch (HttpException exception)
        {
            return Note(nameof(LogAsync), channelId, Translate(exception, channelId));
        }
    }

    private async Task<Result<ulong>> SendAsync(
        ulong channelId,
        MessageComponent view,
        string operation,
        CancellationToken cancellationToken)
    {
        if (Resolve(channelId) is not { } channel)
        {
            return Note<ulong>(operation, channelId, Missing(channelId));
        }

        try
        {
            IUserMessage message = await channel.SendMessageAsync(
                allowedMentions: AllowedMentions.None,
                components: view,
                flags: MessageFlags.ComponentsV2,
                options: Options(cancellationToken));

            return Result<ulong>.Success(message.Id);
        }
        catch (HttpException exception)
        {
            return Note<ulong>(operation, channelId, Translate(exception, channelId));
        }
    }

    private IMessageChannel? Resolve(ulong channelId) =>
        client.GetChannel(channelId) as IMessageChannel;

    // A channel the socket cache does not hold is only genuinely missing when the
    // cache is populated. During a reconnect it holds nothing at all, and calling
    // that a deleted channel would tell an operator to reconfigure a channel that
    // is perfectly fine.
    private Error Missing(ulong channelId) => client.ConnectionState == ConnectionState.Connected
        ? PublishErrors.ChannelNotFound(channelId)
        : PublishErrors.Unavailable;

    // Two statuses out of the whole of HTTP say something an operator can act on:
    // the channel is gone, or the bot is not allowed in it. Written as a chain
    // rather than a switch because naming every other status would suggest the
    // rest had been considered, and they have not — they are all "Discord said no".
    private static Error Translate(HttpException exception, ulong channelId)
    {
        if (exception.HttpCode == HttpStatusCode.NotFound)
        {
            return PublishErrors.ChannelNotFound(channelId);
        }

        return exception.HttpCode == HttpStatusCode.Forbidden
            ? PublishErrors.ChannelForbidden(channelId)
            : PublishErrors.ApiFailure((int)exception.HttpCode);
    }

    private Error Note(string operation, ulong channelId, Error error)
    {
        InteractionLog.PublishFailed(logger, operation, channelId, error.Code);
        return error;
    }

    private Result<T> Note<T>(string operation, ulong channelId, Error error)
    {
        InteractionLog.PublishFailed(logger, operation, channelId, error.Code);
        return Result<T>.Failure(error);
    }

    private static RequestOptions Options(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken,
    };
}
