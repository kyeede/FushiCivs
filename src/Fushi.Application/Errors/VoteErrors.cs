using Fushi.Core.Errors;
using Fushi.Core.Identifiers;

namespace Fushi.Application.Errors;

/// <summary>
/// Failures that can arise while casting or withdrawing a vote.
/// </summary>
public static class VoteErrors
{
    /// <summary>
    /// The caller has not voted on the submission, so there is nothing to
    /// withdraw.
    /// </summary>
    /// <param name="code">The submission's code.</param>
    /// <returns>The failure.</returns>
    public static Error NotCast(ShortCode code) => Error.NotFound(
        "Vote.NotCast",
        $"You have not voted on submission {code}.");

    /// <summary>
    /// The caller has already voted and the guild does not permit changing a
    /// vote.
    /// </summary>
    /// <param name="code">The submission's code.</param>
    /// <returns>The failure.</returns>
    public static Error AlreadyCast(ShortCode code) => Error.Conflict(
        "Vote.AlreadyCast",
        $"You have already voted on submission {code}, and this server does not allow votes "
        + "to be changed.");

    /// <summary>
    /// The vote would not change anything.
    /// </summary>
    /// <remarks>
    /// A failure rather than a silent success, so the user gets told their vote
    /// already stands instead of being shown a confirmation that implies
    /// something happened.
    /// </remarks>
    public static Error Unchanged => Error.Conflict(
        "Vote.Unchanged",
        "That is already how you voted.");

    /// <summary>
    /// The guild does not allow abstentions.
    /// </summary>
    public static Error AbstentionNotAllowed => Error.Validation(
        "Vote.AbstentionNotAllowed",
        "This server does not allow abstaining. Vote for or against, or do not vote.");

    /// <summary>
    /// The caller is the applicant and the guild does not allow self-voting.
    /// </summary>
    public static Error SelfVoteNotAllowed => Error.Forbidden(
        "Vote.SelfVoteNotAllowed",
        "You cannot vote on your own submission.");

    /// <summary>
    /// The vote arrived after voting closed.
    /// </summary>
    /// <remarks>
    /// Reachable even when the cycle's stored status still says open, because the
    /// closing instant passes before the scheduler notices. The vote is refused
    /// on the clock rather than on the status.
    /// </remarks>
    public static Error WindowClosed => Error.Conflict(
        "Vote.WindowClosed",
        "Voting has closed for this cycle.");
}
