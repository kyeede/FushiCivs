namespace Fushi.Core.Abstractions;

/// <summary>
/// Base class for entities compared by identity.
/// </summary>
/// <remarks>
/// Two instances are the same entity when they are the same runtime type and
/// carry the same <see cref="Id"/>, regardless of whether their other values
/// currently agree. That is what makes it safe to hold a detached copy of a row
/// and still recognise it after a reload.
/// <br/>
/// Entities with a default identifier are treated as distinct from everything,
/// including each other. A default identifier means "not yet persisted", and
/// two unsaved objects are not the same row simply because neither has been
/// assigned one.
/// </remarks>
/// <typeparam name="TId">The identifier type.</typeparam>
public abstract class Entity<TId> : IEntity<TId>, IEquatable<Entity<TId>>
    where TId : struct, IEquatable<TId>
{
    /// <summary>
    /// Initialises the entity with its permanent identifier.
    /// </summary>
    /// <param name="id">
    /// The identifier. Must not be the default value of
    /// <typeparamref name="TId"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is the default value of
    /// <typeparamref name="TId"/>.
    /// </exception>
    protected Entity(TId id)
    {
        if (EqualityComparer<TId>.Default.Equals(id, default))
        {
            throw new ArgumentException("An entity identifier cannot be the default value.", nameof(id));
        }

        Id = id;
    }

    /// <summary>
    /// Initialises an entity without an identifier, for materialisation by the
    /// persistence layer.
    /// </summary>
    /// <remarks>
    /// The object relational mapper sets <see cref="Id"/> directly from the
    /// database row, so it needs a constructor that does not demand the value
    /// up front. Application code should always use the public constructor of
    /// the derived type instead.
    /// </remarks>
    protected Entity() { }

    /// <inheritdoc/>
    public TId Id { get; private set; }

    /// <summary>
    /// Determines whether the specified entity is the same entity as this one.
    /// </summary>
    /// <param name="other">The entity to compare against.</param>
    /// <returns>
    /// <see langword="true"/> when both are the same runtime type and share a
    /// non-default identifier; otherwise <see langword="false"/>.
    /// </returns>
    public bool Equals(Entity<TId>? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // A derived type is never equal to its base, so a Submission and a Vote
        // that happen to share a Guid stay distinct.
        if (GetType() != other.GetType())
        {
            return false;
        }

        if (
            EqualityComparer<TId>.Default.Equals(Id, default) || EqualityComparer<TId>.Default.Equals(other.Id, default)
        )
        {
            return false;
        }

        return Id.Equals(other.Id);
    }

    /// <inheritdoc/>
    public sealed override bool Equals(object? obj) => Equals(obj as Entity<TId>);

    /// <inheritdoc/>
    /// <remarks>
    /// The runtime type participates in the hash so that entities of different
    /// types sharing an identifier land in different buckets.
    /// </remarks>
    public sealed override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>
    /// Determines whether two entities are the same entity.
    /// </summary>
    /// <param name="left">The first entity, which may be <see langword="null"/>.</param>
    /// <param name="right">The second entity, which may be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when both are <see langword="null"/> or both refer
    /// to the same entity; otherwise <see langword="false"/>.
    /// </returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>
    /// Determines whether two entities are different entities.
    /// </summary>
    /// <param name="left">The first entity, which may be <see langword="null"/>.</param>
    /// <param name="right">The second entity, which may be <see langword="null"/>.</param>
    /// <returns>The negation of <see cref="op_Equality"/>.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);
}
