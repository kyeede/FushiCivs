namespace Fushi.Core.Abstractions;

/// <summary>
/// Marks an entity as soft-deletable: removal is recorded as state rather than
/// carried out as a physical delete.
/// </summary>
/// <remarks>
/// Votes and submissions are evidence of a decision, so destroying the row
/// would destroy the ability to explain an outcome after the fact. Deleted rows
/// are filtered out of ordinary reads by a global query filter in the
/// persistence layer, which means a handler never has to remember to exclude
/// them.
/// </remarks>
public interface IDeletable
{
    /// <summary>
    /// Gets the instant the entity was deleted, in UTC.
    /// </summary>
    /// <value>
    /// <see langword="null"/> while the entity is live.
    /// </value>
    DateTimeOffset? DeletedAt { get; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor that deleted the entity.
    /// </summary>
    /// <value>
    /// <see langword="null"/> while the entity is live.
    /// </value>
    ulong? DeletedBy { get; }

    /// <summary>
    /// Gets a value indicating whether the entity has been soft-deleted.
    /// </summary>
    /// <value>
    /// <see langword="true"/> once <see cref="DeletedAt"/> has been stamped;
    /// otherwise <see langword="false"/>.
    /// </value>
    bool IsDeleted { get; }
}
