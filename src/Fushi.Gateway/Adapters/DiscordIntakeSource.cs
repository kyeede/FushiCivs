using System.Net;

using Fushi.Application.Abstractions.Discord;
using Fushi.Core.Errors;
using Fushi.Core.Results;
using Fushi.Gateway.Errors;
using Fushi.Gateway.Logging;
using Fushi.Gateway.Options;

using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fushi.Gateway.Adapters;

/// <summary>
/// Reads candidate submissions out of a guild's intake channel.
/// </summary>
/// <remarks>
/// Pulls history rather than listening for message events. Subscribing would lose
/// anything posted while the process was restarting; re-reading from a recorded
/// snowflake costs a restart nothing, and paginating by snowflake rather than by
/// timestamp uses Discord's own cursor, so a message posted in the same second as
/// the last one processed can neither be skipped nor read twice.
/// <br/>
/// Every way this can fail comes back as a <see cref="Result{T}"/> rather than an
/// exception. A deleted channel, a revoked permission, or a rate limit are
/// ordinary events in the life of a bot, and the scheduler that calls this needs
/// to record what happened and carry on rather than unwind.
/// </remarks>
/// <param name="client">The connected socket client.</param>
/// <param name="options">
/// The connection settings, read for the configured page size.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class DiscordIntakeSource(
    DiscordSocketClient client,
    IOptions<DiscordOptions> options,
    ILogger<DiscordIntakeSource> logger)
    : IIntakeSource
{
    /// <summary>
    /// The most messages Discord will return for one history request.
    /// </summary>
    /// <remarks>
    /// Asking for more is not an error and does not fetch more; Discord simply
    /// returns 100 and says nothing. Clamping here makes the ceiling visible
    /// rather than leaving a caller to wonder why their request for 500 produced
    /// a fifth of that.
    /// </remarks>
    private const int MAX_HISTORY_PAGE = 100;

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<IntakeMessage>>> ReadAsync(
        ulong channelId,
        ulong? afterMessageId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Two ceilings, both real. Discord will not return more than 100 whatever
        // is asked of it, and the deployment may want less than that so a busy
        // guild is drained in several small passes rather than one long one.
        int ceiling = Math.Min(options.Value.IntakePageSize, MAX_HISTORY_PAGE);
        int pageSize = Math.Clamp(limit, 1, ceiling);

        RequestOptions request = new()
        {
            CancelToken = cancellationToken,
        };

        try
        {
            // Checks the socket cache and falls back to a REST fetch by itself,
            // which is what makes it correct for a channel the bot has not
            // touched since the process started.
            IChannel? channel = await client.GetChannelAsync(channelId, request);

            if (channel is null)
            {
                return Failed(channelId, GatewayErrors.ChannelNotFound(channelId));
            }

            // A thread and an announcement channel are both ITextChannel, so both
            // fall into the first branch and need nothing of their own. A forum
            // is the exception: it holds no messages at all, only posts, and each
            // post is a thread whose opening message is the application.
            IReadOnlyList<IntakeMessage>? messages = channel switch
            {
                ITextChannel text =>
                    await CollectAsync(text, afterMessageId, pageSize, request, cancellationToken),
                SocketForumChannel forum =>
                    await CollectPostsAsync(forum, afterMessageId, pageSize, request),
                _ => null,
            };

            if (messages is null)
            {
                return Failed(channelId, GatewayErrors.ChannelNotReadable(channelId));
            }

            GatewayLog.IntakeRead(logger, messages.Count, channelId);

            return Result<IReadOnlyList<IntakeMessage>>.Success(messages);
        }
        catch (HttpException exception)
        {
            return Failed(channelId, Translate(exception, channelId));
        }
    }

    /// <summary>
    /// Pages through the channel's history and projects what it finds.
    /// </summary>
    /// <remarks>
    /// The results are ordered by snowflake before being returned, because the
    /// interface promises oldest first and Discord hands history back newest
    /// first. Sorting on the snowflake rather than reversing the sequence gets the
    /// same answer without depending on which order the library happened to
    /// deliver the pages in — a snowflake encodes its own creation time, so
    /// ascending numeric order is chronological order by construction.
    /// </remarks>
    /// <param name="channel">The channel to read.</param>
    /// <param name="afterMessageId">
    /// The last message already processed, or <see langword="null"/> to start from
    /// the oldest message the channel still holds.
    /// </param>
    /// <param name="pageSize">How many messages to read, already clamped.</param>
    /// <param name="request">The request options carrying the cancellation token.</param>
    /// <param name="cancellationToken">Cancelled when the caller stops waiting.</param>
    /// <returns>The messages found, oldest first.</returns>
    private static async Task<IReadOnlyList<IntakeMessage>> CollectAsync(
        ITextChannel channel,
        ulong? afterMessageId,
        int pageSize,
        RequestOptions request,
        CancellationToken cancellationToken)
    {
        // Snowflake zero stands in for "no cursor yet". A snowflake encodes its
        // creation time, so zero predates every message that has ever existed and
        // asking for what comes after it is asking for the start of the channel —
        // which is what the interface promises when no cursor is given. Reading
        // the most recent page instead would be a different and quieter bug: the
        // first pass would work and every application older than it would be
        // skipped for good.
        ulong after = afterMessageId ?? 0UL;

        IAsyncEnumerable<IReadOnlyCollection<IMessage>> pages = channel.GetMessagesAsync(
            after,
            Direction.After,
            pageSize,
            CacheMode.AllowDownload,
            request);

        List<IntakeMessage> collected = [];

        await foreach (IReadOnlyCollection<IMessage> page in pages.WithCancellation(cancellationToken))
        {
            foreach (IMessage message in page)
            {
                collected.Add(Project(message));
            }
        }

        collected.Sort(static (left, right) => left.MessageId.CompareTo(right.MessageId));

        return collected;
    }

    /// <summary>
    /// Reads a forum's posts and projects the opening message of each.
    /// </summary>
    /// <remarks>
    /// A forum post is a thread, and the identifier Discord gives that thread is
    /// the identifier of its opening message. That coincidence is what lets the
    /// same snowflake cursor work here as for a text channel: filtering threads by
    /// identifier filters them by the moment their application was written, and a
    /// post already captured can neither be missed nor read twice.
    /// <br/>
    /// Active posts are read together with a page of archived ones. Discord pages
    /// archived threads by the time they were archived rather than by snowflake,
    /// so the two cannot be walked as one sequence — but a post has to sit
    /// untouched for days before it archives, and the sweeper runs every couple of
    /// minutes, so anything archived before it was seen implies an outage measured
    /// in days rather than an ordinary gap.
    /// </remarks>
    /// <param name="forum">The forum to read.</param>
    /// <param name="afterMessageId">The last post already processed.</param>
    /// <param name="pageSize">How many posts to read, already clamped.</param>
    /// <param name="request">The request options carrying the cancellation token.</param>
    /// <returns>The opening messages found, oldest first.</returns>
    private static async Task<IReadOnlyList<IntakeMessage>> CollectPostsAsync(
        SocketForumChannel forum,
        ulong? afterMessageId,
        int pageSize,
        RequestOptions request)
    {
        ulong after = afterMessageId ?? 0UL;

        IReadOnlyCollection<RestThreadChannel> active = await forum.GetActiveThreadsAsync(request);
        IReadOnlyCollection<RestThreadChannel> archived =
            await forum.GetPublicArchivedThreadsAsync(pageSize, null, request);

        List<RestThreadChannel> posts =
        [
            .. active
                .Concat(archived)
                .Where(thread => thread.Id > after)
                .DistinctBy(thread => thread.Id)
                .OrderBy(thread => thread.Id)
                .Take(pageSize),
        ];

        List<IntakeMessage> collected = [];

        foreach (RestThreadChannel post in posts)
        {
            // The opening message shares the thread's identifier. Asking for it
            // by that identifier is one lookup rather than a history page, and it
            // cannot accidentally pick up a reply somebody left underneath.
            if (await post.GetMessageAsync(post.Id, request) is { } opening)
            {
                collected.Add(Project(opening));
            }
        }

        return collected;
    }

    /// <summary>
    /// Reduces a Discord message to the parts a submission is built from.
    /// </summary>
    /// <remarks>
    /// A webhook counts as a bot. Discord reports the two separately, but for
    /// intake's purposes they are the same thing — something automated posted it,
    /// and treating a webhook relay as an applicant would let any integration in
    /// the channel file applications.
    /// </remarks>
    /// <param name="message">The message to project.</param>
    /// <returns>The projection the application layer sees.</returns>
    private static IntakeMessage Project(IMessage message) => new(
        message.Channel.Id,
        message.Id,
        message.Author.Id,
        message.Content ?? string.Empty,
        message.Timestamp,
        message.Author.IsBot || message.Author.IsWebhook,
        [.. message.Attachments.Select(attachment => attachment.Url)]);

    /// <summary>
    /// Turns a Discord HTTP failure into the error that describes it.
    /// </summary>
    /// <remarks>
    /// A 403 and a 404 mean different things to whoever has to fix the guild's
    /// configuration: one says grant the bot a permission, the other says the
    /// channel is gone. Reporting both as "could not read the channel" would leave
    /// them to work out which by trial.
    /// </remarks>
    /// <param name="exception">The failure Discord returned.</param>
    /// <param name="channelId">The channel that was being read.</param>
    /// <returns>The error to return to the caller.</returns>
    private Error Translate(HttpException exception, ulong channelId)
    {
        GatewayLog.ApiFailed(
            logger,
            (int)exception.HttpCode,
            nameof(ReadAsync),
            exception.Reason ?? exception.Message,
            exception);

        if (exception.HttpCode == HttpStatusCode.Forbidden)
        {
            return GatewayErrors.ChannelForbidden(channelId);
        }

        return exception.HttpCode == HttpStatusCode.NotFound
            ? GatewayErrors.ChannelNotFound(channelId)
            : GatewayErrors.ApiFailure((int)exception.HttpCode);
    }

    /// <summary>
    /// Logs a read failure and wraps it as a result.
    /// </summary>
    /// <param name="channelId">The channel that could not be read.</param>
    /// <param name="error">The failure to report.</param>
    /// <returns>A failed result carrying <paramref name="error"/>.</returns>
    private Result<IReadOnlyList<IntakeMessage>> Failed(ulong channelId, Error error)
    {
        GatewayLog.IntakeReadFailed(logger, channelId, error.Code);

        return Result<IReadOnlyList<IntakeMessage>>.Failure(error);
    }
}
