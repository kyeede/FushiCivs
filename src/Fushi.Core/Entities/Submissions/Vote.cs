using Fushi.Core.Abstractions;
using Fushi.Core.Utilities;

namespace Fushi.Core.Entities.Submissions;

/// <summary>
/// One voter's decision on one submission.
/// </summary>
/// <remarks>
/// Changing a vote overwrites <see cref="Choice"/> on the existing row rather
/// than inserting a second one, so that the count is a straightforward query
/// and cannot double-count anybody. The audit trail carries the history of what
/// changed, which is the right place for it: the vote row answers "how does this
/// person stand now", and that has exactly one answer at a time.
/// <br/>
/// Votes are soft-deleted rather than removed, because a submission's outcome
/// has to remain explainable after a voter's access is revoked.
/// </remarks>
public sealed class Vote : AuditableEntity<Guid>
{
    /// <summary>
    /// The longest comment a voter may attach, sized to fit inside a Discord
    /// embed field alongside the voter's name.
    /// </summary>
    public const int MAX_COMMENT_LENGTH = 512;

    /// <summary>
    /// Initialises a vote.
    /// </summary>
    /// <param name="id">The permanent identifier.</param>
    /// <param name="submissionId">The submission being voted on.</param>
    /// <param name="voterId">The voting user's snowflake.</param>
    /// <param name="choice">The decision.</param>
    /// <param name="castAt">The instant the vote was cast.</param>
    /// <param name="comment">An optional justification.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> or <paramref name="submissionId"/> is empty, or
    /// <paramref name="choice"/> is not a defined value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="voterId"/> is <c>0</c>, or <paramref name="comment"/>
    /// exceeds <see cref="MAX_COMMENT_LENGTH"/>.
    /// </exception>
    public Vote(
        Guid id,
        Guid submissionId,
        ulong voterId,
        VoteChoice choice,
        DateTimeOffset castAt,
        string? comment = null)
        : base(id, castAt, voterId)
    {
        ArgumentOutOfRangeException.ThrowIfZero(voterId);

        if (submissionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A vote must belong to a submission.",
                nameof(submissionId));
        }

        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentException($"'{choice}' is not a defined choice.", nameof(choice));
        }

        SubmissionId = submissionId;
        VoterId = voterId;
        Choice = choice;
        Comment = Sanitise(comment);
    }

    private Vote()
    {
    }

    /// <summary>
    /// Gets the submission being voted on.
    /// </summary>
    public Guid SubmissionId { get; private set; }

    /// <summary>
    /// Gets the voting user's snowflake.
    /// </summary>
    public ulong VoterId { get; private set; }

    /// <summary>
    /// Gets the current decision.
    /// </summary>
    public VoteChoice Choice { get; private set; }

    /// <summary>
    /// Gets the justification the voter attached.
    /// </summary>
    /// <value>
    /// The comment, or <see langword="null"/> when none was given.
    /// </value>
    public string? Comment { get; private set; }

    /// <summary>
    /// Gets the number of times the voter has revised this vote.
    /// </summary>
    /// <remarks>
    /// Surfaced so that a results summary can note a vote was changed without
    /// having to join against the audit trail.
    /// </remarks>
    public int RevisionCount { get; private set; }

    /// <summary>
    /// Gets a Discord mention for the voter.
    /// </summary>
    public string Mention => MentionUtility.User(VoterId);

    /// <summary>
    /// Gets a value indicating whether this vote counts towards the quorum.
    /// </summary>
    public bool IsDeciding => Choice is VoteChoice.Approve or VoteChoice.Reject;

    /// <summary>
    /// Creates a vote with a freshly generated identifier.
    /// </summary>
    /// <remarks>
    /// The identifier is a version 7 GUID, which embeds the creation timestamp
    /// in its high bits. Inserts therefore land at the end of the index instead
    /// of scattering across it, which matters for a table that only ever grows.
    /// </remarks>
    /// <param name="submissionId">The submission being voted on.</param>
    /// <param name="voterId">The voting user's snowflake.</param>
    /// <param name="choice">The decision.</param>
    /// <param name="castAt">The instant the vote was cast.</param>
    /// <param name="comment">An optional justification.</param>
    /// <returns>The new vote.</returns>
    public static Vote Create(
        Guid submissionId,
        ulong voterId,
        VoteChoice choice,
        DateTimeOffset castAt,
        string? comment = null)
        => new(Guid.CreateVersion7(castAt), submissionId, voterId, choice, castAt, comment);

    /// <summary>
    /// Revises the decision.
    /// </summary>
    /// <remarks>
    /// Whether revision is allowed at all is a policy question answered before
    /// this is called; the entity only refuses moves that are impossible rather
    /// than ones that are disallowed.
    /// </remarks>
    /// <param name="choice">The new decision.</param>
    /// <param name="revisedAt">The instant of the revision.</param>
    /// <param name="comment">
    /// The new justification, or <see langword="null"/> to clear it.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when something changed; <see langword="false"/>
    /// when the new decision and comment match the current ones.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="choice"/> is not a defined value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="comment"/> exceeds <see cref="MAX_COMMENT_LENGTH"/>.
    /// </exception>
    public bool Revise(VoteChoice choice, DateTimeOffset revisedAt, string? comment = null)
    {
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentException($"'{choice}' is not a defined choice.", nameof(choice));
        }

        string? sanitised = Sanitise(comment);
        if (Choice == choice && Comment == sanitised)
        {
            return false;
        }

        Choice = choice;
        Comment = sanitised;
        RevisionCount++;
        MarkUpdated(revisedAt, VoterId);

        return true;
    }

    private static string? Sanitise(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        string trimmed = comment.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            trimmed.Length,
            MAX_COMMENT_LENGTH,
            nameof(comment));

        return trimmed;
    }
}
