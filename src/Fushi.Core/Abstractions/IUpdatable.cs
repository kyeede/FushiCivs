namespace Fushi.Core.Abstractions;

/// <summary>
/// Records the most recent modification made to an entity.
/// </summary>
/// <remarks>
/// Only the latest modification is kept. The full history of who changed what
/// lives in the audit trail rather than on the entity, so that the row stays a
/// fixed size no matter how often it is edited.
/// </remarks>
public interface IUpdatable
{
    /// <summary>
    /// Gets the instant of the most recent modification, in UTC.
    /// </summary>
    /// <value>
    /// <see langword="null"/> when the entity has never been modified since
    /// creation.
    /// </value>
    DateTimeOffset? UpdatedAt { get; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor behind the most recent
    /// modification.
    /// </summary>
    /// <value>
    /// <see langword="null"/> when the entity has never been modified since
    /// creation.
    /// </value>
    ulong? UpdatedBy { get; }
}
