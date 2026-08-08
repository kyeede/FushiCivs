namespace Fushi.Core.Entities.Submissions;

/// <summary>
/// The verdict a closed vote produced.
/// </summary>
/// <remarks>
/// Distinct from <see cref="SubmissionStatus"/>, which tracks where a
/// submission is in its lifecycle. A submission that has not been voted on yet
/// has a status but no outcome.
/// </remarks>
/// <seealso cref="Fushi.Core.Entities.Guilds.VotingPolicy.Evaluate"/>
public enum SubmissionOutcome
{
    /// <summary>
    /// Enough people voted, and enough of them approved.
    /// </summary>
    Approved = 0,

    /// <summary>
    /// Enough people voted, and too few of them approved.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// Too few people voted for the result to mean anything. The submission was
    /// not judged, and deliberately does not count as a rejection.
    /// </summary>
    Skipped = 2,
}
