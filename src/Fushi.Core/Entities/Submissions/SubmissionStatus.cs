namespace Fushi.Core.Entities.Submissions;

/// <summary>
/// Where a submission sits in its lifecycle.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="SubmissionOutcome"/>. This says how far
/// along the submission is; the outcome says what the vote decided. Merging them
/// would make "queued" and "approved" mutually exclusive values of one field,
/// and then there would be nowhere to record that a decided submission was
/// approved rather than merely finished.
/// </remarks>
public enum SubmissionStatus
{
    /// <summary>
    /// Captured from the intake channel but not yet accepted into a cycle.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Accepted and waiting for a cycle to open.
    /// </summary>
    Queued = 1,

    /// <summary>
    /// Attached to an open cycle and accepting votes.
    /// </summary>
    UnderReview = 2,

    /// <summary>
    /// Judged. <see cref="Submission.Outcome"/> holds the verdict.
    /// </summary>
    Decided = 3,

    /// <summary>
    /// Taken back by the applicant, or removed by a moderator, before a
    /// decision. Terminal.
    /// </summary>
    Withdrawn = 4,
}
