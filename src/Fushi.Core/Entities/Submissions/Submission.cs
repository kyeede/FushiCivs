using Fushi.Core.Abstractions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Identifiers;
using Fushi.Core.Utilities;

namespace Fushi.Core.Entities.Submissions;

/// <summary>
/// An application collected from a guild's intake channel, and the votes cast on
/// it.
/// </summary>
/// <remarks>
/// A submission outlives the cycle it is judged in. It is captured whenever the
/// applicant posts, waits in <see cref="SubmissionStatus.Queued"/> for the next
/// cycle to open, and returns to the queue if that cycle is cancelled. The
/// origin message is recorded so the original post stays reachable even after
/// the submission has been copied into a review channel and edited there.
/// <br/>
/// <see cref="Code"/> is what people use to address it. The
/// <see cref="Abstractions.Entity{TId}.Id"/> exists for joins and never appears
/// in the interface.
/// </remarks>
/// <seealso cref="SubmissionStatus"/>
/// <seealso cref="SubmissionOutcome"/>
public sealed class Submission : AuditableEntity<Guid>
{
    /// <summary>
    /// The longest title accepted, matching Discord's own embed title limit so
    /// that a stored title always renders in full.
    /// </summary>
    public const int MAX_TITLE_LENGTH = 256;

    /// <summary>
    /// The longest body accepted. Below Discord's 4,096 character embed
    /// description limit, leaving room for the footer and vote summary the
    /// renderer appends.
    /// </summary>
    public const int MAX_CONTENT_LENGTH = 3_800;

    private readonly List<Vote> _votes = [];

    /// <summary>
    /// Initialises a captured submission.
    /// </summary>
    /// <param name="id">The permanent identifier.</param>
    /// <param name="code">The public reference code.</param>
    /// <param name="guildId">The guild the submission was made in.</param>
    /// <param name="applicantId">The applying user's snowflake.</param>
    /// <param name="sourceChannelId">The intake channel it came from.</param>
    /// <param name="sourceMessageId">The originating message.</param>
    /// <param name="title">A short summary.</param>
    /// <param name="content">The body of the application.</param>
    /// <param name="createdAt">The instant it was captured.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is empty, <paramref name="code"/> is
    /// <see cref="ShortCode.Empty"/>, or <paramref name="title"/> or
    /// <paramref name="content"/> is empty or white space.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A snowflake argument is <c>0</c>, or <paramref name="title"/> or
    /// <paramref name="content"/> exceeds its length limit.
    /// </exception>
    public Submission(
        Guid id,
        ShortCode code,
        ulong guildId,
        ulong applicantId,
        ulong sourceChannelId,
        ulong sourceMessageId,
        string title,
        string content,
        DateTimeOffset createdAt)
        : base(id, createdAt, applicantId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(guildId);
        ArgumentOutOfRangeException.ThrowIfZero(applicantId);
        ArgumentOutOfRangeException.ThrowIfZero(sourceChannelId);
        ArgumentOutOfRangeException.ThrowIfZero(sourceMessageId);

        if (code.IsEmpty)
        {
            throw new ArgumentException("A submission requires a public code.", nameof(code));
        }

        Code = code;
        GuildId = guildId;
        ApplicantId = applicantId;
        SourceChannelId = sourceChannelId;
        SourceMessageId = sourceMessageId;
        Title = Require(title, MAX_TITLE_LENGTH, nameof(title));
        Content = Require(content, MAX_CONTENT_LENGTH, nameof(content));
        Status = SubmissionStatus.Draft;
    }

    private Submission()
    {
    }

    /// <summary>
    /// Gets the public short code users type to address this submission.
    /// </summary>
    public ShortCode Code { get; private set; }

    /// <summary>
    /// Gets the guild the submission was made in.
    /// </summary>
    public ulong GuildId { get; private set; }

    /// <summary>
    /// Gets the cycle currently judging this submission.
    /// </summary>
    /// <value>
    /// The cycle identifier, or <see langword="null"/> while the submission is
    /// waiting in the queue.
    /// </value>
    public Guid? CycleId { get; private set; }

    /// <summary>
    /// Gets the applying user's snowflake.
    /// </summary>
    public ulong ApplicantId { get; private set; }

    /// <summary>
    /// Gets the intake channel the submission was collected from.
    /// </summary>
    public ulong SourceChannelId { get; private set; }

    /// <summary>
    /// Gets the message the submission was collected from.
    /// </summary>
    public ulong SourceMessageId { get; private set; }

    /// <summary>
    /// Gets the short summary shown in lists and embed titles.
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the body of the application.
    /// </summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the message posted in the review channel that voters react to.
    /// </summary>
    /// <value>
    /// The message snowflake, or <see langword="null"/> before it has been
    /// posted.
    /// </value>
    public ulong? ReviewMessageId { get; private set; }

    /// <summary>
    /// Gets the discussion thread opened alongside the review message.
    /// </summary>
    /// <value>
    /// The thread snowflake, or <see langword="null"/> when no thread was
    /// created.
    /// </value>
    public ulong? ThreadId { get; private set; }

    /// <summary>
    /// Gets the current lifecycle state.
    /// </summary>
    public SubmissionStatus Status { get; private set; }

    /// <summary>
    /// Gets the verdict the vote produced.
    /// </summary>
    /// <value>
    /// The outcome, or <see langword="null"/> until
    /// <see cref="Status"/> is <see cref="SubmissionStatus.Decided"/>.
    /// </value>
    public SubmissionOutcome? Outcome { get; private set; }

    /// <summary>
    /// Gets the instant the verdict was recorded.
    /// </summary>
    /// <value>
    /// The instant, or <see langword="null"/> while undecided.
    /// </value>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>
    /// Gets the actor that recorded the verdict.
    /// </summary>
    /// <value>
    /// The actor's snowflake, <c>0</c> when the scheduler decided it
    /// automatically, or <see langword="null"/> while undecided.
    /// </value>
    public ulong? DecidedBy { get; private set; }

    /// <summary>
    /// Gets the votes cast on this submission.
    /// </summary>
    public IReadOnlyCollection<Vote> Votes => _votes;

    /// <summary>
    /// Gets a Discord mention for the applicant.
    /// </summary>
    public string Mention => MentionUtility.User(ApplicantId);

    /// <summary>
    /// Gets the current vote counts.
    /// </summary>
    /// <remarks>
    /// Recomputed on each read from the loaded votes. Callers that need the
    /// tally more than once in a row should hold the value rather than reading
    /// the property repeatedly.
    /// </remarks>
    public VoteTally Tally => VoteTally.From(_votes);

    /// <summary>
    /// Gets a value indicating whether the submission has reached a state it
    /// cannot leave.
    /// </summary>
    public bool IsTerminal => Status is SubmissionStatus.Decided or SubmissionStatus.Withdrawn;

    /// <summary>
    /// Creates a submission with a freshly generated identifier and code.
    /// </summary>
    /// <param name="guildId">The guild the submission was made in.</param>
    /// <param name="applicantId">The applying user's snowflake.</param>
    /// <param name="sourceChannelId">The intake channel it came from.</param>
    /// <param name="sourceMessageId">The originating message.</param>
    /// <param name="title">A short summary.</param>
    /// <param name="content">The body of the application.</param>
    /// <param name="createdAt">The instant it was captured.</param>
    /// <returns>The new submission.</returns>
    public static Submission Create(
        ulong guildId,
        ulong applicantId,
        ulong sourceChannelId,
        ulong sourceMessageId,
        string title,
        string content,
        DateTimeOffset createdAt)
        => new(
            Guid.CreateVersion7(createdAt),
            ShortCode.New(),
            guildId,
            applicantId,
            sourceChannelId,
            sourceMessageId,
            title,
            content,
            createdAt);

    /// <summary>
    /// Replaces the public code, for the rare case that a generated one
    /// collided with an existing row.
    /// </summary>
    /// <remarks>
    /// Called only by the persistence layer's retry path. A code is otherwise
    /// permanent, because people write them down.
    /// </remarks>
    /// <param name="code">The replacement code.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="code"/> is <see cref="ShortCode.Empty"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The submission has already been shown to voters, so its code is in
    /// circulation and cannot be changed.
    /// </exception>
    public void ReassignCode(ShortCode code)
    {
        if (code.IsEmpty)
        {
            throw new ArgumentException("A submission requires a public code.", nameof(code));
        }

        if (ReviewMessageId.HasValue)
        {
            throw new InvalidOperationException(
                "The code of a submission that has already been published cannot be changed.");
        }

        Code = code;
    }

    /// <summary>
    /// Edits the title and body.
    /// </summary>
    /// <param name="title">The new summary.</param>
    /// <param name="content">The new body.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="title"/> or <paramref name="content"/> is empty or white
    /// space.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="title"/> or <paramref name="content"/> exceeds its
    /// length limit.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The submission has already been decided or withdrawn.
    /// </exception>
    public void Revise(string title, string content, DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A {Status} submission cannot be edited.");
        }

        Title = Require(title, MAX_TITLE_LENGTH, nameof(title));
        Content = Require(content, MAX_CONTENT_LENGTH, nameof(content));
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Accepts the submission into the queue, making it eligible for the next
    /// cycle.
    /// </summary>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor accepting it.</param>
    /// <exception cref="InvalidOperationException">
    /// The submission is not a draft.
    /// </exception>
    public void Queue(DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (Status != SubmissionStatus.Draft)
        {
            throw new InvalidOperationException($"A {Status} submission cannot be queued.");
        }

        Status = SubmissionStatus.Queued;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Attaches the submission to an open cycle and puts it under review.
    /// </summary>
    /// <param name="cycleId">The cycle that will judge it.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="cycleId"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The submission is not queued.
    /// </exception>
    public void PutUnderReview(Guid cycleId, DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (cycleId == Guid.Empty)
        {
            throw new ArgumentException("A cycle identifier is required.", nameof(cycleId));
        }

        if (Status != SubmissionStatus.Queued)
        {
            throw new InvalidOperationException(
                $"A {Status} submission cannot be put under review.");
        }

        CycleId = cycleId;
        Status = SubmissionStatus.UnderReview;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Detaches the submission from its cycle and returns it to the queue.
    /// </summary>
    /// <remarks>
    /// Used when a cycle is cancelled. Votes already cast are cleared, because
    /// they were cast under a cycle that no longer counts and carrying them into
    /// the next one would let a voter's decision apply twice.
    /// </remarks>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="InvalidOperationException">
    /// The submission has already been decided or withdrawn.
    /// </exception>
    public void ReturnToQueue(DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A {Status} submission cannot be requeued.");
        }

        foreach (Vote vote in _votes)
        {
            vote.MarkDeleted(updatedAt, updatedBy);
        }

        CycleId = null;
        Status = SubmissionStatus.Queued;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Records where the submission was published for voting.
    /// </summary>
    /// <param name="messageId">The review message snowflake.</param>
    /// <param name="threadId">
    /// The discussion thread snowflake, or <see langword="null"/> when no thread
    /// was created.
    /// </param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="messageId"/> is <c>0</c>.
    /// </exception>
    public void SetReviewMessage(
        ulong messageId,
        ulong? threadId,
        DateTimeOffset updatedAt,
        ulong updatedBy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(messageId);

        ReviewMessageId = messageId;
        ThreadId = threadId is 0uL ? null : threadId;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Finds a voter's live vote on this submission.
    /// </summary>
    /// <param name="voterId">The voting user's snowflake.</param>
    /// <returns>
    /// The vote, or <see langword="null"/> when that voter has not voted.
    /// </returns>
    public Vote? FindVote(ulong voterId)
        => _votes.Find(vote => vote.VoterId == voterId && !vote.IsDeleted);

    /// <summary>
    /// Records a vote, revising the voter's existing one when they have already
    /// voted.
    /// </summary>
    /// <remarks>
    /// Whether the voter is permitted to vote, and whether revision is allowed,
    /// are decided before this is called: those questions need the guild's
    /// policy and the caller's roles, neither of which the submission knows
    /// about. What this guarantees is the part it can see — that one voter never
    /// ends up with two live votes.
    /// </remarks>
    /// <param name="voterId">The voting user's snowflake.</param>
    /// <param name="choice">The decision.</param>
    /// <param name="castAt">The instant the vote was cast.</param>
    /// <param name="comment">An optional justification.</param>
    /// <returns>
    /// The vote that now represents this voter's position, whether newly
    /// created or revised.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The submission is not under review.
    /// </exception>
    public Vote RecordVote(
        ulong voterId,
        VoteChoice choice,
        DateTimeOffset castAt,
        string? comment = null)
    {
        if (Status != SubmissionStatus.UnderReview)
        {
            throw new InvalidOperationException(
                $"Votes cannot be cast on a {Status} submission.");
        }

        if (FindVote(voterId) is { } existing)
        {
            _ = existing.Revise(choice, castAt, comment);
            return existing;
        }

        Vote vote = Vote.Create(Id, voterId, choice, castAt, comment);
        _votes.Add(vote);

        return vote;
    }

    /// <summary>
    /// Removes a voter's vote.
    /// </summary>
    /// <param name="voterId">The voting user's snowflake.</param>
    /// <param name="retractedAt">The instant of the retraction.</param>
    /// <returns>
    /// <see langword="true"/> when a vote was removed; <see langword="false"/>
    /// when that voter had not voted.
    /// </returns>
    public bool RetractVote(ulong voterId, DateTimeOffset retractedAt)
    {
        if (FindVote(voterId) is not { } vote)
        {
            return false;
        }

        vote.MarkDeleted(retractedAt, voterId);
        return true;
    }

    /// <summary>
    /// Applies a guild's rules to the votes cast so far.
    /// </summary>
    /// <remarks>
    /// Does not change the submission. Use it to preview an outcome while voting
    /// is open, then <see cref="Decide"/> to commit one.
    /// </remarks>
    /// <param name="policy">The rules to apply.</param>
    /// <returns>The outcome those votes would produce.</returns>
    public SubmissionOutcome Evaluate(VotingPolicy policy) => policy.Evaluate(Tally);

    /// <summary>
    /// Records the verdict, ending the submission's review.
    /// </summary>
    /// <param name="outcome">The verdict.</param>
    /// <param name="decidedAt">The instant the verdict was reached.</param>
    /// <param name="decidedBy">
    /// The deciding actor, or <c>0</c> when the scheduler decided it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="outcome"/> is not a defined value.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The submission is not under review.
    /// </exception>
    public void Decide(SubmissionOutcome outcome, DateTimeOffset decidedAt, ulong decidedBy)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentException($"'{outcome}' is not a defined outcome.", nameof(outcome));
        }

        if (Status != SubmissionStatus.UnderReview)
        {
            throw new InvalidOperationException($"A {Status} submission cannot be decided.");
        }

        Status = SubmissionStatus.Decided;
        Outcome = outcome;
        DecidedAt = decidedAt;
        DecidedBy = decidedBy;
        MarkUpdated(decidedAt, decidedBy);
    }

    /// <summary>
    /// Withdraws the submission before it is judged.
    /// </summary>
    /// <param name="withdrawnAt">The instant of the withdrawal.</param>
    /// <param name="withdrawnBy">
    /// The applicant, or a moderator acting on their behalf.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The submission has already been decided or withdrawn.
    /// </exception>
    public void Withdraw(DateTimeOffset withdrawnAt, ulong withdrawnBy)
    {
        if (IsTerminal)
        {
            throw new InvalidOperationException($"A {Status} submission cannot be withdrawn.");
        }

        Status = SubmissionStatus.Withdrawn;
        MarkUpdated(withdrawnAt, withdrawnBy);
    }

    private static string Require(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        string trimmed = value.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, maxLength, parameterName);

        return trimmed;
    }
}
