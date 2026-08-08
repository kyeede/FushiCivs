namespace Fushi.Core.Entities.Submissions;

/// <summary>
/// What a voter decided about a submission.
/// </summary>
/// <seealso cref="Vote"/>
public enum VoteChoice
{
    /// <summary>
    /// The voter is in favour. Counts towards both the approval share and the
    /// quorum.
    /// </summary>
    Approve = 0,

    /// <summary>
    /// The voter is against. Counts towards the quorum and against the approval
    /// share.
    /// </summary>
    Reject = 1,

    /// <summary>
    /// The voter read the submission and declined to judge it. Recorded as
    /// participation but excluded from both the approval share and the quorum,
    /// so an abstention can never decide an outcome.
    /// </summary>
    Abstain = 2,
}
