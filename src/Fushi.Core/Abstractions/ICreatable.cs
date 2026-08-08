namespace Fushi.Core.Abstractions;

/// <summary>
/// Records who brought an entity into existence and when.
/// </summary>
/// <remarks>
/// Split from <see cref="IUpdatable"/> and <see cref="IDeletable"/> so that
/// persistence interceptors can stamp each concern independently: creation is
/// written exactly once, whereas modification is rewritten on every save.
/// </remarks>
public interface ICreatable
{
    /// <summary>
    /// Gets the instant the entity was created, in UTC.
    /// </summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Gets the Discord user snowflake of the actor that created the entity.
    /// </summary>
    /// <value>
    /// The acting user's snowflake, or <c>0</c> when the entity originated from
    /// the bot itself rather than from a human action.
    /// </value>
    ulong CreatedBy { get; }
}
