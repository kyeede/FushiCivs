using Fushi.Core.Errors;

namespace Fushi.Application.Errors;

/// <summary>
/// Failures that can arise while granting or revoking voting rights.
/// </summary>
public static class PermissionErrors
{
    /// <summary>
    /// The grant referred to does not exist.
    /// </summary>
    public static Error NotFound => Error.NotFound(
        "Permission.NotFound",
        "That user or role does not have voting rights here, so there is nothing to revoke.");

    /// <summary>
    /// The same user or role already has voting rights.
    /// </summary>
    /// <param name="mention">The user or role, as Discord mention markup.</param>
    /// <returns>The failure.</returns>
    public static Error AlreadyGranted(string mention) => Error.Conflict(
        "Permission.AlreadyGranted",
        $"{mention} can already vote here.");

    /// <summary>
    /// The caller may not vote in this guild.
    /// </summary>
    /// <remarks>
    /// Voting is deny-by-default: the absence of a grant is the ordinary case,
    /// not an anomaly. The description says so plainly, because a user who has
    /// never been given rights should not be left wondering whether something is
    /// broken.
    /// </remarks>
    public static Error CannotVote => Error.Forbidden(
        "Permission.CannotVote",
        "You have not been given voting rights on this server. Ask a moderator if you think "
        + "you should have them.");

    /// <summary>
    /// The bot could not establish which roles the caller holds, so it cannot
    /// tell whether they may vote.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="CannotVote"/> on purpose. Treating an unanswered
    /// question as a refusal would silently deny legitimate voters whenever
    /// Discord was slow, and they would have no way to tell the difference.
    /// </remarks>
    public static Error Undetermined => Error.Unexpected(
        "Permission.Undetermined",
        "Your roles could not be checked just now. Please try again in a moment.");
}
