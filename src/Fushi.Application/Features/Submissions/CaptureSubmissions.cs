using Fushi.Application.Abstractions.Discord;
using Fushi.Application.Abstractions.Messaging;
using Fushi.Application.Abstractions.Persistence;
using Fushi.Application.Errors;
using Fushi.Application.Logging;
using Fushi.Core.Entities.Audits;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;
using Fushi.Core.Results;

using FluentValidation;

using Microsoft.Extensions.Logging;

namespace Fushi.Application.Features.Submissions;

/// <summary>
/// Reads a guild's intake channel and captures everything in it that is not a
/// submission already.
/// </summary>
/// <remarks>
/// Intake pulls rather than subscribing to message events. Listening for posts
/// as they arrive would be less work, but a message posted while the process was
/// restarting or disconnected would be lost for good, and nothing would ever
/// notice it was missing. Re-reading the channel from a recorded position costs
/// one history request and makes a restart free: anything that was posted while
/// the bot was away is still there to be read.
/// <br/>
/// The same property makes the command safe to repeat. Every message is checked
/// against <see cref="ISubmissionRepository.ExistsForMessageAsync"/>, so running
/// a sweep twice over the same range captures nothing the second time.
/// </remarks>
/// <param name="GuildId">The guild whose intake channel is to be read.</param>
/// <param name="ActorId">
/// The user who asked for the sweep, or <c>0</c> when the scheduler ran it on
/// its own initiative.
/// </param>
/// <param name="AfterMessageId">
/// The last message already dealt with, or <see langword="null"/> to start from
/// the oldest message the channel still holds.
/// </param>
/// <param name="Limit">
/// The most messages to read in this pass, at most
/// <see cref="MAX_LIMIT"/>.
/// </param>
public sealed record CaptureSubmissions(
    ulong GuildId,
    ulong ActorId,
    ulong? AfterMessageId = null,
    int Limit = 50) : ICommand<IntakeSummaryModel>
{
    /// <summary>
    /// The most messages one pass may read, matching the cap Discord places on
    /// a single channel history request.
    /// </summary>
    public const int MAX_LIMIT = 100;
}

/// <summary>
/// Checks the shape of a <see cref="CaptureSubmissions"/> command.
/// </summary>
/// <remarks>
/// Whether the guild has an intake channel, and whether the bot can read it,
/// need the database and Discord respectively. Both are the handler's to
/// establish.
/// </remarks>
internal sealed class CaptureSubmissionsValidator : AbstractValidator<CaptureSubmissions>
{
    /// <summary>
    /// Initialises the rule set.
    /// </summary>
    public CaptureSubmissionsValidator()
    {
        RuleFor(command => command.GuildId)
            .NotEqual(0uL)
            .WithMessage("A guild is required.");

        RuleFor(command => command.Limit)
            .InclusiveBetween(1, CaptureSubmissions.MAX_LIMIT)
            .WithMessage($"Read between 1 and {CaptureSubmissions.MAX_LIMIT} messages at a time.");
    }
}

/// <summary>
/// Carries out <see cref="CaptureSubmissions"/>.
/// </summary>
/// <remarks>
/// Each capture goes through <see cref="SubmissionStatus.Draft"/> and straight on
/// to <see cref="SubmissionStatus.Queued"/>, which looks redundant while intake
/// accepts everything it reads. It is not.
/// <see cref="SubmissionStatus.Draft"/> is where a submission sits when a
/// moderator has to accept it before it can be voted on, and keeping the state
/// occupied means adding that review step later is a change to this handler
/// alone rather than a migration over every submission ever stored.
/// </remarks>
/// <param name="guilds">The guild store.</param>
/// <param name="submissions">The submission store.</param>
/// <param name="intake">The reader for the guild's intake channel.</param>
/// <param name="codes">Allocates the public codes new submissions are given.</param>
/// <param name="audit">The audit trail.</param>
/// <param name="clock">
/// Supplies the current instant. <see cref="TimeProvider"/> rather than a bespoke
/// clock interface: it is the framework's own abstraction, and tests can
/// substitute a fake without this project defining one.
/// </param>
/// <param name="logger">The logger to write to.</param>
internal sealed class CaptureSubmissionsHandler(
    IGuildRepository guilds,
    ISubmissionRepository submissions,
    IIntakeSource intake,
    IShortCodeAllocator codes,
    IAuditWriter audit,
    TimeProvider clock,
    ILogger<CaptureSubmissionsHandler> logger)
    : ICommandHandler<CaptureSubmissions, IntakeSummaryModel>
{
    // Two allocations colliding inside one sweep is vanishingly unlikely, but
    // the allocator checks against committed rows only and this handler stages
    // several inserts before any of them commit. A handful of retries closes
    // that window; anything beyond it means something is wrong rather than
    // unlucky.
    private const int MAX_CODE_ATTEMPTS = 4;

    /// <inheritdoc/>
    public async Task<Result<IntakeSummaryModel>> HandleAsync(
        CaptureSubmissions request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guild? guild = await guilds.FindAsync(request.GuildId, cancellationToken);
        if (guild is null)
        {
            return GuildErrors.NotFound;
        }

        if (!guild.IsEnabled)
        {
            return GuildErrors.Disabled;
        }

        if (guild.Channels.IntakeChannelId is not { } intakeChannelId)
        {
            return GuildErrors.NotConfigured;
        }

        SubmissionLog.SweepStarted(
            logger,
            request.GuildId,
            intakeChannelId,
            request.AfterMessageId);

        Result<IReadOnlyList<IntakeMessage>> read = await intake.ReadAsync(
            intakeChannelId,
            request.AfterMessageId,
            request.Limit,
            cancellationToken);

        if (read.IsFailure)
        {
            return read.Error;
        }

        IReadOnlyList<IntakeMessage> messages = read.Value;
        DateTimeOffset now = clock.GetUtcNow();
        HashSet<ShortCode> allocated = [];

        int captured = 0;
        int skipped = 0;

        foreach (IntakeMessage message in messages)
        {
            if (!message.IsCandidate)
            {
                skipped++;
                SubmissionLog.MessageSkipped(
                    logger,
                    message.MessageId,
                    message.IsFromBot ? "posted by a bot" : "no text to read");
                continue;
            }

            bool known = await submissions.ExistsForMessageAsync(
                request.GuildId,
                message.MessageId,
                cancellationToken);

            if (known)
            {
                skipped++;
                SubmissionLog.MessageSkipped(logger, message.MessageId, "already captured");
                continue;
            }

            ShortCode code = await AllocateAsync(request.GuildId, allocated, cancellationToken);
            if (code.IsEmpty)
            {
                // Stopping rather than skipping. The message keeps its place, so
                // the next pass reaches it again; skipping would consume it and
                // lose the submission entirely.
                SubmissionLog.SweepHalted(
                    logger,
                    request.GuildId,
                    "no unused short code could be allocated");
                break;
            }

            (string title, string content) = Derive(message);

            Submission submission = new(
                Guid.CreateVersion7(now),
                code,
                request.GuildId,
                message.AuthorId,
                message.ChannelId,
                message.MessageId,
                title,
                content,
                now);

            submission.Queue(now, request.ActorId);
            submissions.Add(submission);

            // One entry rather than two. Capturing and queueing happen in the
            // same breath here, so a "created" entry followed immediately by a
            // "queued" entry would double the trail without adding a fact.
            audit.Record(AuditEntry.Record(
                request.GuildId,
                AuditScope.Submission,
                AuditAction.SubmissionQueued,
                now,
                request.ActorId,
                subjectId: submission.Id,
                subjectCode: code,
                targetId: message.AuthorId));

            string rendered = code.ToString();
            SubmissionLog.Captured(
                logger,
                request.GuildId,
                rendered,
                message.AuthorId,
                message.MessageId);
            SubmissionLog.Queued(logger, request.GuildId, rendered, request.ActorId);

            captured++;
        }

        SubmissionLog.SweepFinished(logger, request.GuildId, messages.Count, captured, skipped);

        return new IntakeSummaryModel(messages.Count, captured, skipped);
    }

    private async Task<ShortCode> AllocateAsync(
        ulong guildId,
        HashSet<ShortCode> allocated,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MAX_CODE_ATTEMPTS; attempt++)
        {
            ShortCode code = await codes.AllocateForSubmissionAsync(guildId, cancellationToken);
            if (allocated.Add(code))
            {
                return code;
            }

            SubmissionLog.CodeCollisionRetried(logger, guildId, code.ToString(), attempt);
        }

        return ShortCode.Empty;
    }

    private static (string Title, string Content) Derive(IntakeMessage message)
    {
        ReadOnlySpan<char> text = message.Content.AsSpan().Trim();
        int breakAt = text.IndexOf('\n');

        ReadOnlySpan<char> headline = (breakAt < 0 ? text : text[..breakAt]).Trim();
        ReadOnlySpan<char> remainder = breakAt < 0
            ? ReadOnlySpan<char>.Empty
            : text[(breakAt + 1)..].Trim();

        // A one-line post still needs a body, because a submission cannot be
        // stored without one. Reusing the single line it has is honest: that
        // really is everything the applicant wrote.
        ReadOnlySpan<char> body = remainder.IsEmpty ? headline : remainder;

        // Attachment links are appended rather than dropped so that an image an
        // application depends on stays reachable from the submission after the
        // original post has scrolled away or been deleted. They are measured
        // first so that a long body is what gets cut, never a truncated URL.
        string attachments = Attachments(message.AttachmentUrls);
        string content = Clamp(body, Submission.MAX_CONTENT_LENGTH - attachments.Length)
            + attachments;

        return (
            Clamp(headline, Submission.MAX_TITLE_LENGTH),
            Clamp(content.AsSpan(), Submission.MAX_CONTENT_LENGTH));
    }

    private static string Attachments(IReadOnlyList<string> urls)
    {
        string joined = string.Join(
            '\n',
            urls.Where(static url => !string.IsNullOrWhiteSpace(url)));

        return joined.Length == 0 ? string.Empty : "\n" + joined;
    }

    private static string Clamp(ReadOnlySpan<char> value, int maxLength) => maxLength <= 0
        ? string.Empty
        : new string(value.Length <= maxLength ? value : value[..maxLength]);
}

/// <summary>
/// What one pass over an intake channel did.
/// </summary>
/// <remarks>
/// Counted rather than listed. A sweep can read a hundred messages and the
/// caller is a scheduler or a moderator who wants to know that it worked, not
/// the contents of each one.
/// </remarks>
/// <param name="MessagesRead">How many messages the channel returned.</param>
/// <param name="Captured">How many became new submissions.</param>
/// <param name="Skipped">
/// How many were passed over, whether because a bot posted them, because they
/// carried no text, or because they had already been captured.
/// </param>
public sealed record IntakeSummaryModel(int MessagesRead, int Captured, int Skipped);
