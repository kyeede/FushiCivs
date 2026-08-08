namespace Fushi.Core.Entities.Cycles;

/// <summary>
/// Where a voting cycle sits in its lifecycle.
/// </summary>
/// <remarks>
/// Progress is one-way through <see cref="Scheduled"/>,
/// <see cref="Open"/>, <see cref="Closed"/>, and <see cref="Finalised"/>, with
/// <see cref="Cancelled"/> reachable from anywhere except the end.
/// <see cref="Cycle.TransitionTo"/> is the only way to move between them and
/// rejects anything else, so a cycle cannot reopen after its results have been
/// published.
/// </remarks>
public enum CycleStatus
{
    /// <summary>
    /// Created from the schedule but not yet accepting votes.
    /// </summary>
    Scheduled = 0,

    /// <summary>
    /// Accepting votes.
    /// </summary>
    Open = 1,

    /// <summary>
    /// No longer accepting votes, but outcomes have not been applied yet. A
    /// cycle sits here between the closing instant and the job that evaluates
    /// it, which is a real interval rather than a theoretical one.
    /// </summary>
    Closed = 2,

    /// <summary>
    /// Outcomes have been applied to every submission and the results have been
    /// published. Terminal.
    /// </summary>
    Finalised = 3,

    /// <summary>
    /// Abandoned before finalisation. Submissions return to the queue for a
    /// later cycle rather than being judged. Terminal.
    /// </summary>
    Cancelled = 4,
}
