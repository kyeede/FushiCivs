namespace Fushi.Core.Entities.Audits;

/// <summary>
/// The kind of record an audit entry concerns.
/// </summary>
/// <remarks>
/// Kept separate from the identifier of the affected row so that the trail can
/// be filtered by area without joining anything. "Show me every permission
/// change in this guild" is then a single indexed read.
/// </remarks>
/// <seealso cref="AuditEntry"/>
public enum AuditScope
{
    /// <summary>
    /// The guild's own configuration: channels, policy, schedule, or whether the
    /// bot is enabled.
    /// </summary>
    Guild = 0,

    /// <summary>
    /// A voting grant.
    /// </summary>
    Permission = 1,

    /// <summary>
    /// A voting cycle.
    /// </summary>
    Cycle = 2,

    /// <summary>
    /// A submission.
    /// </summary>
    Submission = 3,

    /// <summary>
    /// An individual vote.
    /// </summary>
    Vote = 4,
}
