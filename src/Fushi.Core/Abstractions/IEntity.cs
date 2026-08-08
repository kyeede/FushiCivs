namespace Fushi.Core.Abstractions;

/// <summary>
/// An entity identified by a value of type <typeparamref name="TId"/>.
/// </summary>
/// <typeparam name="TId">
/// The identifier type. Constrained to a value type so an entity can never
/// carry a null identity, and to <see cref="IEquatable{T}"/> so identity
/// comparison avoids boxing.
/// </typeparam>
public interface IEntity<out TId>
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
