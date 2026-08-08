using Fushi.Core.Entities.Cycles;
using Fushi.Core.Errors;
using Fushi.Core.Identifiers;

namespace Fushi.Application.Errors;

/// <summary>
/// Failures that can arise while running a voting cycle.
/// </summary>
public static class CycleErrors
{
    /// <summary>
    /// No cycle exists with the code given.
    /// </summary>
    /// <param name="code">The code the user supplied.</param>
    /// <returns>The failure.</returns>
    public static Error NotFound(ShortCode code) => Error.NotFound(
        "Cycle.NotFound",
        $"No cycle here has the code {code}. Check the code and try again.");

    /// <summary>
    /// Voting is not in progress.
    /// </summary>
    public static Error NoneOpen => Error.NotFound(
        "Cycle.NoneOpen",
        "Voting is not open at the moment. Run /cycle status to see when it next opens.");

    /// <summary>
    /// A cycle is already open, so another cannot be.
    /// </summary>
    /// <param name="code">The code of the cycle already running.</param>
    /// <returns>The failure.</returns>
    public static Error AlreadyOpen(ShortCode code) => Error.Conflict(
        "Cycle.AlreadyOpen",
        $"Cycle {code} is already open. Close it before opening another.");

    /// <summary>
    /// Today is not one of the guild's configured voting days.
    /// </summary>
    public static Error NotACycleDay => Error.Conflict(
        "Cycle.NotACycleDay",
        "Today is not one of this server's voting days. Change them with /config schedule, or "
        + "open a cycle manually.");

    /// <summary>
    /// The requested move through the cycle's lifecycle is not possible.
    /// </summary>
    /// <param name="from">The state the cycle is in.</param>
    /// <param name="to">The state that was requested.</param>
    /// <returns>The failure.</returns>
    public static Error InvalidTransition(CycleStatus from, CycleStatus to) => Error.Conflict(
        "Cycle.InvalidTransition",
        $"A cycle that is {Describe(from)} cannot be {Describe(to)}.");

    /// <summary>
    /// The cycle has finished and cannot be changed further.
    /// </summary>
    /// <param name="code">The cycle's code.</param>
    /// <returns>The failure.</returns>
    public static Error Concluded(ShortCode code) => Error.Conflict(
        "Cycle.Concluded",
        $"Cycle {code} has already finished. Its results cannot be changed.");

    /// <summary>
    /// There is nothing queued to vote on.
    /// </summary>
    /// <remarks>
    /// Opening an empty cycle is refused rather than allowed, because the
    /// announcement would invite people to vote on nothing and the cycle would
    /// have to be cancelled by hand afterwards.
    /// </remarks>
    public static Error NothingQueued => Error.Conflict(
        "Cycle.NothingQueued",
        "There are no submissions waiting, so there is nothing to vote on yet.");

    /// <summary>
    /// The cycle stopped accepting votes but has not been evaluated, so its
    /// results are not final.
    /// </summary>
    /// <param name="code">The cycle's code.</param>
    /// <returns>The failure.</returns>
    public static Error NotYetEvaluated(ShortCode code) => Error.Conflict(
        "Cycle.NotYetEvaluated",
        $"Cycle {code} has closed but its outcomes have not been worked out yet. Try again "
        + "shortly, or run /cycle finalise.");

    private static string Describe(CycleStatus status) => status switch
    {
        CycleStatus.Scheduled => "scheduled",
        CycleStatus.Open => "open",
        CycleStatus.Closed => "closed",
        CycleStatus.Finalised => "finalised",
        CycleStatus.Cancelled => "cancelled",
        _ => status.ToString(),
    };
}
