namespace Fushi.Core.Abstractions;

/// <summary>
/// An <see cref="Entity{TId}"/> that carries a creation, modification, and
/// soft-deletion stamp.
/// </summary>
/// <remarks>
/// Every mutation of a moderated system needs an answer to "who did this, and
/// when". Rather than leave that to each call site, the stamps live on the
/// entity and are written through the three <c>Mark</c> methods, which refuse
/// to record a timeline that could not have happened.
/// <br/>
/// Clock values are supplied by the caller rather than read from
/// <see cref="DateTimeOffset.UtcNow"/> here, so that a handler under test can
/// drive the timeline deterministically through its own time provider.
/// </remarks>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class AuditableEntity<TId> : Entity<TId>
    where TId : struct, IEquatable<TId>
{
    /// <summary>
    /// Initialises the entity and stamps its creation.
    /// </summary>
    /// <param name="id">The permanent identifier.</param>
    /// <param name="createdAt">The creation instant, in UTC.</param>
    /// <param name="createdBy">
    /// The Discord user snowflake of the creating actor, or <c>0</c> when the
    /// bot created the entity on its own initiative.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is the default value of
    /// <typeparamref name="TId"/>.
    /// </exception>
    protected AuditableEntity(TId id, DateTimeOffset createdAt, ulong createdBy)
        : base(id)
    {
        CreatedAt = createdAt;
        CreatedBy = createdBy;
    }

    /// <inheritdoc cref="Entity{TId}()"/>
    protected AuditableEntity() { }

    /// <summary>
    /// Gets the instant the entity was created, in UTC.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor that created the entity.
    /// </summary>
    /// <value>
    /// The acting user's snowflake, or <c>0</c> when the entity originated from
    /// the bot itself rather than from a human action.
    /// </value>
    public ulong CreatedBy { get; private set; }

    /// <summary>
    /// Gets the instant of the most recent modification, in UTC.
    /// </summary>
    /// <value>
    /// <see langword="null"/> when the entity has never been modified since
    /// creation.
    /// </value>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor behind the most recent
    /// modification.
    /// </summary>
    /// <value>
    /// <see langword="null"/> when the entity has never been modified since
    /// creation.
    /// </value>
    public ulong? UpdatedBy { get; private set; }

    /// <summary>
    /// Gets the instant the entity was deleted, in UTC.
    /// </summary>
    /// <value>
    /// <see langword="null"/> while the entity is live.
    /// </value>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor that deleted the entity.
    /// </summary>
    /// <value>
    /// <see langword="null"/> while the entity is live.
    /// </value>
    public ulong? DeletedBy { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the entity has been soft-deleted.
    /// </summary>
    public bool IsDeleted => DeletedAt.HasValue;

    /// <summary>
    /// Stamps a modification by the given actor at the given instant.
    /// </summary>
    /// <param name="updatedAt">The modification instant, in UTC.</param>
    /// <param name="updatedBy">
    /// The Discord user snowflake of the modifying actor.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="updatedAt"/> precedes <see cref="CreatedAt"/>.
    /// </exception>
    public void MarkUpdated(DateTimeOffset updatedAt, ulong updatedBy)
    {
        if (updatedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(updatedAt),
                updatedAt,
                "An entity cannot be modified before it was created."
            );
        }

        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    /// <summary>
    /// Stamps a soft deletion by the given actor at the given instant.
    /// </summary>
    /// <remarks>
    /// Deleting an already-deleted entity is a no-op rather than an error, so
    /// that a retried command cannot overwrite the original deletion stamp and
    /// lose the identity of whoever actually performed it.
    /// </remarks>
    /// <param name="deletedAt">The deletion instant, in UTC.</param>
    /// <param name="deletedBy">
    /// The Discord user snowflake of the deleting actor.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="deletedAt"/> precedes <see cref="CreatedAt"/>.
    /// </exception>
    public void MarkDeleted(DateTimeOffset deletedAt, ulong deletedBy)
    {
        if (deletedAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletedAt),
                deletedAt,
                "An entity cannot be deleted before it was created."
            );
        }

        if (IsDeleted)
        {
            return;
        }

        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }

    /// <summary>
    /// Clears the soft-deletion stamp and records the restoration as a
    /// modification.
    /// </summary>
    /// <param name="restoredAt">The restoration instant, in UTC.</param>
    /// <param name="restoredBy">
    /// The Discord user snowflake of the restoring actor.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="restoredAt"/> precedes <see cref="CreatedAt"/>.
    /// </exception>
    public void MarkRestored(DateTimeOffset restoredAt, ulong restoredBy)
    {
        if (!IsDeleted)
        {
            return;
        }

        DeletedAt = null;
        DeletedBy = null;
        MarkUpdated(restoredAt, restoredBy);
    }
}
