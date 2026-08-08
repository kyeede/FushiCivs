namespace Fushi.Core.Abstractions;

/// <summary>
/// Marks a type as a persisted entity whose lifetime is tracked by identity
/// rather than by value.
/// </summary>
/// <remarks>
/// The non-generic form exists so that infrastructure can discover and
/// configure every entity through a single reflection pass without knowing
/// the closed identifier type of each one. Prefer <see cref="IEntity{TId}"/>
/// in application code, which keeps the identifier strongly typed.
/// </remarks>
public interface IEntity;

/// <summary>
/// An entity identified by a value of type <typeparamref name="TId"/>.
/// </summary>
/// <typeparam name="TId">
/// The identifier type. Constrained to a value type so an entity can never
/// carry a null identity, and to <see cref="IEquatable{T}"/> so identity
/// comparison avoids boxing.
/// </typeparam>
public interface IEntity<out TId> : IEntity
    where TId : struct, IEquatable<TId>
{
    /// <summary>
    /// Gets the identifier that distinguishes this entity from every other
    /// instance of the same type.
    /// </summary>
    /// <value>
    /// A stable, non-default value assigned once at construction and never
    /// reassigned for the lifetime of the row.
    /// </value>
    TId Id { get; }
}
