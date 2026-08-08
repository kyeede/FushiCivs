using Fushi.Core.Abstractions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Identifiers;

namespace Fushi.Core.Entities.Cycles;

/// <summary>
/// One occurrence of a guild's voting schedule, with the submissions it judges.
/// </summary>
/// <remarks>
/// A cycle is created from a <see cref="CycleSchedule"/> but does not keep a
/// reference to it. Instead it copies the resolved opening and closing instants
/// and the <see cref="VotingPolicy"/> that applied when it was created.
/// Rescheduling or raising the pass threshold afterwards therefore cannot change
/// the terms of a vote that is already under way, and a result stays explainable
/// months later from the row alone.
/// </remarks>
/// <seealso cref="CycleWindow"/>
public sealed class Cycle : AuditableEntity<Guid>
{
    private readonly List<Submission> _submissions = [];

    /// <summary>
    /// Initialises a scheduled cycle.
    /// </summary>
    /// <param name="id">The permanent identifier.</param>
    /// <param name="code">The public reference code.</param>
    /// <param name="guildId">The guild the cycle belongs to.</param>
    /// <param name="window">The resolved voting window.</param>
    /// <param name="policy">
    /// The voting rules to apply, copied from the guild at creation time.
    /// </param>
    /// <param name="createdAt">The instant the cycle was created.</param>
    /// <param name="createdBy">
    /// The actor that created it, or <c>0</c> when the scheduler did.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is empty, or <paramref name="code"/> is
    /// <see cref="ShortCode.Empty"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> is <c>0</c>.
    /// </exception>
    public Cycle(
        Guid id,
        ShortCode code,
        ulong guildId,
        CycleWindow window,
        VotingPolicy policy,
        DateTimeOffset createdAt,
        ulong createdBy)
        : base(id, createdAt, createdBy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(guildId);

        if (code.IsEmpty)
        {
            throw new ArgumentException("A cycle requires a public code.", nameof(code));
        }

        Code = code;
        GuildId = guildId;
        ScheduledDate = window.Date;
        OpensAt = window.OpensAt;
        ClosesAt = window.ClosesAt;
        Policy = policy;
        Status = CycleStatus.Scheduled;
    }

    private Cycle()
    {
    }

    /// <summary>
    /// Gets the public short code users type to address this cycle.
    /// </summary>
    public ShortCode Code { get; private set; }

    /// <summary>
    /// Gets the guild this cycle belongs to.
    /// </summary>
    public ulong GuildId { get; private set; }

    /// <summary>
    /// Gets the local date the cycle is labelled with.
    /// </summary>
    public DateOnly ScheduledDate { get; private set; }

    /// <summary>
    /// Gets the instant voting opens.
    /// </summary>
    public DateTimeOffset OpensAt { get; private set; }

    /// <summary>
    /// Gets the instant voting closes.
    /// </summary>
    public DateTimeOffset ClosesAt { get; private set; }

    /// <summary>
    /// Gets the voting rules as they stood when the cycle was created.
    /// </summary>
    public VotingPolicy Policy { get; private set; }

    /// <summary>
    /// Gets the current lifecycle state.
    /// </summary>
    public CycleStatus Status { get; private set; }

    /// <summary>
    /// Gets the message announcing that voting has opened.
    /// </summary>
    /// <value>
    /// The message snowflake, or <see langword="null"/> before the
    /// announcement has been posted.
    /// </value>
    public ulong? AnnouncementMessageId { get; private set; }

    /// <summary>
    /// Gets the message publishing the outcomes.
    /// </summary>
    /// <value>
    /// The message snowflake, or <see langword="null"/> before results have
    /// been published.
    /// </value>
    public ulong? ResultsMessageId { get; private set; }

    /// <summary>
    /// Gets the submissions this cycle judges.
    /// </summary>
    public IReadOnlyCollection<Submission> Submissions => _submissions;

    /// <summary>
    /// Gets the window this cycle runs over.
    /// </summary>
    public CycleWindow Window => new(ScheduledDate, OpensAt, ClosesAt);

    /// <summary>
    /// Gets a value indicating whether the cycle has finished for good.
    /// </summary>
    public bool IsTerminal => Status is CycleStatus.Finalised or CycleStatus.Cancelled;

    /// <summary>
    /// Determines whether the cycle is accepting votes at an instant.
    /// </summary>
    /// <remarks>
    /// Both the recorded status and the clock must agree. The status can lag
    /// the clock by however long it takes the scheduler to notice, and a vote
    /// arriving in that gap is late even though the row still says
    /// <see cref="CycleStatus.Open"/>.
    /// </remarks>
    /// <param name="instant">The instant to test.</param>
    /// <returns>
    /// <see langword="true"/> when a vote cast at that instant should be
    /// accepted.
    /// </returns>
    public bool IsAcceptingVotes(DateTimeOffset instant)
        => Status == CycleStatus.Open && !IsDeleted && Window.Contains(instant);

    /// <summary>
    /// Creates a cycle with a freshly generated identifier and code.
    /// </summary>
    /// <param name="guildId">The guild the cycle belongs to.</param>
    /// <param name="window">The resolved voting window.</param>
    /// <param name="policy">The voting rules to copy.</param>
    /// <param name="createdAt">The instant the cycle was created.</param>
    /// <param name="createdBy">The actor that created it.</param>
    /// <returns>The new cycle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> is <c>0</c>.
    /// </exception>
    public static Cycle Create(
        ulong guildId,
        CycleWindow window,
        VotingPolicy policy,
        DateTimeOffset createdAt,
        ulong createdBy)
        => new(
            Guid.CreateVersion7(createdAt),
            ShortCode.New(),
            guildId,
            window,
            policy,
            createdAt,
            createdBy);

    /// <summary>
    /// Moves the cycle to a new lifecycle state.
    /// </summary>
    /// <remarks>
    /// Repeating the state the cycle is already in is a no-op, so a retried
    /// scheduler pass cannot fail. Any other unlisted move throws, because it
    /// means the caller's model of the cycle is wrong rather than that a user
    /// did something unusual.
    /// </remarks>
    /// <param name="status">The state to move to.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="InvalidOperationException">
    /// The move is not permitted from the current state.
    /// </exception>
    public void TransitionTo(CycleStatus status, DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (Status == status)
        {
            return;
        }

        bool permitted = (Status, status) switch
        {
            (CycleStatus.Scheduled, CycleStatus.Open) => true,
            (CycleStatus.Open, CycleStatus.Closed) => true,
            (CycleStatus.Closed, CycleStatus.Finalised) => true,
            (CycleStatus.Scheduled or CycleStatus.Open or CycleStatus.Closed,
                CycleStatus.Cancelled) => true,
            _ => false,
        };

        if (!permitted)
        {
            throw new InvalidOperationException(
                $"A cycle cannot move from {Status} to {status}.");
        }

        Status = status;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Records the snowflake of the message announcing that voting has opened.
    /// </summary>
    /// <param name="messageId">The announcement message snowflake.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="messageId"/> is <c>0</c>.
    /// </exception>
    public void SetAnnouncementMessage(
        ulong messageId,
        DateTimeOffset updatedAt,
        ulong updatedBy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(messageId);

        AnnouncementMessageId = messageId;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Records the snowflake of the message publishing the outcomes.
    /// </summary>
    /// <param name="messageId">The results message snowflake.</param>
    /// <param name="updatedAt">The instant of the change.</param>
    /// <param name="updatedBy">The actor making the change.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="messageId"/> is <c>0</c>.
    /// </exception>
    public void SetResultsMessage(ulong messageId, DateTimeOffset updatedAt, ulong updatedBy)
    {
        ArgumentOutOfRangeException.ThrowIfZero(messageId);

        ResultsMessageId = messageId;
        MarkUpdated(updatedAt, updatedBy);
    }

    /// <summary>
    /// Attaches a submission to this cycle.
    /// </summary>
    /// <param name="submission">The submission to attach.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="submission"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="submission"/> belongs to a different guild.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The cycle has already finished.
    /// </exception>
    public void Attach(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (submission.GuildId != GuildId)
        {
            throw new ArgumentException(
                "The submission belongs to a different guild.",
                nameof(submission));
        }

        if (IsTerminal)
        {
            throw new InvalidOperationException(
                $"Submissions cannot be added to a {Status} cycle.");
        }

        if (!_submissions.Contains(submission))
        {
            _submissions.Add(submission);
        }
    }
}
