namespace Fushi.Core.Entities.Audits;

/// <summary>
/// What happened, in an audit entry.
/// </summary>
/// <remarks>
/// Values are explicitly numbered and never reused. An audit trail is read long
/// after it is written, so renumbering would silently rewrite history: rows
/// stored under an old number would come back meaning something else. Retire a
/// value by leaving its number unused rather than by filling the gap.
/// </remarks>
/// <seealso cref="AuditEntry"/>
public enum AuditAction
{
    /// <summary>A record was created.</summary>
    Created = 0,

    /// <summary>A record was modified.</summary>
    Updated = 1,

    /// <summary>A record was soft-deleted.</summary>
    Deleted = 2,

    /// <summary>A soft-deleted record was brought back.</summary>
    Restored = 3,

    /// <summary>The bot was switched on for a guild.</summary>
    Enabled = 10,

    /// <summary>The bot was switched off for a guild.</summary>
    Disabled = 11,

    /// <summary>Channel routing was changed.</summary>
    ChannelsConfigured = 12,

    /// <summary>Voting rules were changed.</summary>
    PolicyConfigured = 13,

    /// <summary>The recurring schedule was changed.</summary>
    ScheduleConfigured = 14,

    /// <summary>A voting grant was added.</summary>
    PermissionGranted = 20,

    /// <summary>A voting grant was removed.</summary>
    PermissionRevoked = 21,

    /// <summary>A cycle began accepting votes.</summary>
    CycleOpened = 30,

    /// <summary>A cycle stopped accepting votes.</summary>
    CycleClosed = 31,

    /// <summary>A cycle's outcomes were applied and published.</summary>
    CycleFinalised = 32,

    /// <summary>A cycle was abandoned before finalisation.</summary>
    CycleCancelled = 33,

    /// <summary>A submission was accepted into the queue.</summary>
    SubmissionQueued = 40,

    /// <summary>A submission was attached to an open cycle.</summary>
    SubmissionUnderReview = 41,

    /// <summary>A submission was taken back before a decision.</summary>
    SubmissionWithdrawn = 42,

    /// <summary>A submission passed its vote.</summary>
    SubmissionApproved = 43,

    /// <summary>A submission failed its vote.</summary>
    SubmissionRejected = 44,

    /// <summary>A submission did not reach quorum and was not judged.</summary>
    SubmissionSkipped = 45,

    /// <summary>A vote was cast.</summary>
    VoteCast = 50,

    /// <summary>An existing vote was changed.</summary>
    VoteRevised = 51,

    /// <summary>A vote was withdrawn.</summary>
    VoteRetracted = 52,
}
