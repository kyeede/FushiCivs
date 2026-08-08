using Fushi.Core.Identifiers;

namespace Fushi.Core.Abstractions;

/// <summary>
/// An entity that a user can address by a short public code in addition to its
/// internal primary key.
/// </summary>
/// <remarks>
/// Implemented by the entities that appear in commands people type. The
/// contract lets a lookup be written once against the interface rather than
/// repeated per entity, and it is what tells the persistence layer which tables
/// need the unique index that makes the code dependable.
/// </remarks>
/// <seealso cref="ShortCode"/>
public interface ICodedEntity : IEntity
{
    /// <summary>
    /// Gets the public code that identifies this entity to users.
    /// </summary>
    /// <value>
    /// A non-empty code, unique among live entities of the same type within the
    /// same guild.
    /// </value>
    ShortCode Code { get; }
}
