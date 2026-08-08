using Fushi.Core.Abstractions;
using Fushi.Core.Identifiers;

namespace Fushi.Core.Entities.Audits;

/// <summary>
/// An immutable record of one action taken in a guild.
/// </summary>
/// <remarks>
/// This is the answer to "why is this submission rejected when I approved it".
/// The entities themselves keep only their latest state and the identity of
/// whoever last touched them; the trail is what preserves the sequence.
/// <br/>
/// Entries are written once and never modified, which is why the type derives
/// from <see cref="Entity{TId}"/> rather than
/// <see cref="AuditableEntity{TId}"/>: an audit of the audit would be circular,
/// and there is no legitimate reason to edit one. Deleting is likewise not
/// offered — pruning old entries is an operational task carried out against the
/// table directly, not something a command can be persuaded to do.
/// </remarks>
/// <seealso cref="AuditScope"/>
/// <seealso cref="AuditAction"/>
public sealed class AuditEntry : Entity<Guid>
{
    /// <summary>
    /// The longest reason accepted.
    /// </summary>
    public const int MAX_REASON_LENGTH = 512;

    /// <summary>
    /// Initialises an entry.
    /// </summary>
    /// <param name="id">The permanent identifier.</param>
    /// <param name="guildId">The guild the action took place in.</param>
    /// <param name="scope">The kind of record affected.</param>
    /// <param name="action">What happened.</param>
    /// <param name="createdAt">The instant it happened.</param>
    /// <param name="createdBy">
    /// The acting user's snowflake, or <c>0</c> when the bot acted on its own.
    /// </param>
    /// <param name="subjectId">
    /// The identifier of the affected record, when it has one.
    /// </param>
    /// <param name="subjectCode">
    /// The public code of the affected record, so the trail stays readable
    /// without resolving identifiers.
    /// </param>
    /// <param name="targetId">
    /// The Discord snowflake the action was aimed at, such as the user a grant
    /// was made to.
    /// </param>
    /// <param name="reason">The reason the actor supplied.</param>
    /// <param name="metadata">
    /// A JSON object holding whatever else is worth keeping, such as the values
    /// before and after a configuration change.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is empty, or <paramref name="scope"/> or
    /// <paramref name="action"/> is not a defined value.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="guildId"/> is <c>0</c>, or <paramref name="reason"/>
    /// exceeds <see cref="MAX_REASON_LENGTH"/>.
    /// </exception>
    public AuditEntry(
        Guid id,
        ulong guildId,
        AuditScope scope,
        AuditAction action,
        DateTimeOffset createdAt,
        ulong createdBy,
        Guid? subjectId = null,
        ShortCode? subjectCode = null,
        ulong? targetId = null,
        string? reason = null,
        string? metadata = null)
        : base(id)
    {
        ArgumentOutOfRangeException.ThrowIfZero(guildId);

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentException($"'{scope}' is not a defined scope.", nameof(scope));
        }

        if (!Enum.IsDefined(action))
        {
            throw new ArgumentException($"'{action}' is not a defined action.", nameof(action));
        }

        GuildId = guildId;
        Scope = scope;
        Action = action;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        SubjectId = subjectId == Guid.Empty ? null : subjectId;
        SubjectCode = subjectCode is { IsEmpty: true } ? null : subjectCode;
        TargetId = targetId is 0uL ? null : targetId;
        Reason = Sanitise(reason);
        Metadata = string.IsNullOrWhiteSpace(metadata) ? null : metadata;
    }

    private AuditEntry()
    {
    }

    /// <summary>
    /// Gets the guild the action took place in.
    /// </summary>
    public ulong GuildId { get; private set; }

    /// <summary>
    /// Gets the kind of record affected.
    /// </summary>
    public AuditScope Scope { get; private set; }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public AuditAction Action { get; private set; }

    /// <summary>
    /// Gets the instant the entry was created, in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor that created the entry.
    /// </summary>
    /// <value>
    /// The acting user's snowflake, or <c>0</c> when the bot recorded the entry
    /// on its own initiative.
    /// </value>
    public ulong CreatedBy { get; private set; }

    /// <summary>
    /// Gets the identifier of the affected record.
    /// </summary>
    /// <value>
    /// The identifier, or <see langword="null"/> for an action that has no
    /// single subject row.
    /// </value>
    public Guid? SubjectId { get; private set; }

    /// <summary>
    /// Gets the public code of the affected record.
    /// </summary>
    /// <remarks>
    /// Copied rather than joined, so that an entry still reads sensibly after
    /// the record it refers to has been pruned.
    /// </remarks>
    /// <value>
    /// The code, or <see langword="null"/> when the subject has none.
    /// </value>
    public ShortCode? SubjectCode { get; private set; }

    /// <summary>
    /// Gets the Discord snowflake the action was aimed at.
    /// </summary>
    /// <value>
    /// The snowflake of the affected user, role, or channel, or
    /// <see langword="null"/> when the action had no such target.
    /// </value>
    public ulong? TargetId { get; private set; }

    /// <summary>
    /// Gets the reason the actor supplied.
    /// </summary>
    /// <value>
    /// The reason, or <see langword="null"/> when none was given.
    /// </value>
    public string? Reason { get; private set; }

    /// <summary>
    /// Gets additional context as a JSON object.
    /// </summary>
    /// <value>
    /// The JSON text, or <see langword="null"/> when no extra context was
    /// recorded.
    /// </value>
    public string? Metadata { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the bot performed this action on its own
    /// initiative rather than on someone's instruction.
    /// </summary>
    public bool IsAutomated => CreatedBy == 0uL;

    /// <summary>
    /// Creates an entry with a freshly generated identifier.
    /// </summary>
    /// <param name="guildId">The guild the action took place in.</param>
    /// <param name="scope">The kind of record affected.</param>
    /// <param name="action">What happened.</param>
    /// <param name="createdAt">The instant it happened.</param>
    /// <param name="createdBy">The acting user's snowflake, or <c>0</c>.</param>
    /// <param name="subjectId">The identifier of the affected record.</param>
    /// <param name="subjectCode">The public code of the affected record.</param>
    /// <param name="targetId">The snowflake the action was aimed at.</param>
    /// <param name="reason">The reason the actor supplied.</param>
    /// <param name="metadata">Additional context as JSON.</param>
    /// <returns>The new entry.</returns>
    public static AuditEntry Record(
        ulong guildId,
        AuditScope scope,
        AuditAction action,
        DateTimeOffset createdAt,
        ulong createdBy,
        Guid? subjectId = null,
        ShortCode? subjectCode = null,
        ulong? targetId = null,
        string? reason = null,
        string? metadata = null)
        => new(
            Guid.CreateVersion7(createdAt),
            guildId,
            scope,
            action,
            createdAt,
            createdBy,
            subjectId,
            subjectCode,
            targetId,
            reason,
            metadata);

    private static string? Sanitise(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        string trimmed = reason.Trim();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            trimmed.Length,
            MAX_REASON_LENGTH,
            nameof(reason));

        return trimmed;
    }
}
