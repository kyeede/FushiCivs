namespace Fushi.Core.Abstractions;

/// <summary>
/// Produces the Discord mention markup that renders this entity as a clickable
/// reference inside a message.
/// </summary>
/// <remarks>
/// Implemented by entities that wrap a Discord snowflake and therefore have a
/// canonical rendering, so that presentation code never has to reconstruct the
/// <c>&lt;@id&gt;</c> or <c>&lt;@&amp;id&gt;</c> syntax by hand and get the
/// sigil wrong.
/// </remarks>
/// <seealso cref="Fushi.Core.Utilities.MentionUtility"/>
public interface IMentionable
{
    /// <summary>
    /// Gets the Discord mention markup for this entity.
    /// </summary>
    /// <value>
    /// A string such as <c>&lt;@123&gt;</c> for a user or <c>&lt;@&amp;456&gt;</c>
    /// for a role, ready to embed directly in message content.
    /// </value>
    string Mention { get; }
}
