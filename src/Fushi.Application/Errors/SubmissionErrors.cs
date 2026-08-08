using Fushi.Core.Entities.Submissions;
using Fushi.Core.Errors;
using Fushi.Core.Identifiers;

namespace Fushi.Application.Errors;

/// <summary>
/// Failures that can arise while handling a submission.
/// </summary>
public static class SubmissionErrors
{
    /// <summary>
    /// No submission exists with the code given.
    /// </summary>
    /// <param name="code">The code the user supplied.</param>
    /// <returns>The failure.</returns>
    public static Error NotFound(ShortCode code) => Error.NotFound(
        "Submission.NotFound",
        $"No submission here has the code {code}. Codes are six characters, like 7K4M2P.");

    /// <summary>
    /// The text supplied could not be parsed as a code.
    /// </summary>
    /// <remarks>
    /// Reported separately from <see cref="NotFound(ShortCode)"/> because the
    /// remedy differs: a mistyped code needs correcting, whereas a well-formed
    /// code that matches nothing means the submission is gone or belongs to
    /// another server.
    /// </remarks>
    /// <param name="value">What the user typed.</param>
    /// <returns>The failure.</returns>
    public static Error MalformedCode(string value) => Error.Validation(
        "Submission.MalformedCode",
        $"'{value}' is not a valid code. Codes are six characters using digits and letters, "
        + "excluding I, L, O and U.");

    /// <summary>
    /// The message has already been captured as a submission.
    /// </summary>
    /// <param name="code">The code of the existing submission.</param>
    /// <returns>The failure.</returns>
    public static Error AlreadyCaptured(ShortCode code) => Error.Conflict(
        "Submission.AlreadyCaptured",
        $"That message was already collected as submission {code}.");

    /// <summary>
    /// The submission is not in a state that permits the requested change.
    /// </summary>
    /// <param name="code">The submission's code.</param>
    /// <param name="status">The state it is in.</param>
    /// <returns>The failure.</returns>
    public static Error WrongState(ShortCode code, SubmissionStatus status) => Error.Conflict(
        "Submission.WrongState",
        $"Submission {code} is {Describe(status)}, so that cannot be done to it now.");

    /// <summary>
    /// The submission has already been judged.
    /// </summary>
    /// <param name="code">The submission's code.</param>
    /// <param name="outcome">The verdict it received.</param>
    /// <returns>The failure.</returns>
    public static Error AlreadyDecided(ShortCode code, SubmissionOutcome outcome)
        => Error.Conflict(
            "Submission.AlreadyDecided",
            $"Submission {code} was already {Describe(outcome)}.");

    /// <summary>
    /// The caller is neither the applicant nor a moderator.
    /// </summary>
    public static Error NotYours => Error.Forbidden(
        "Submission.NotYours",
        "You can only withdraw your own submission, unless you can manage the server.");

    /// <summary>
    /// The submission is not attached to any cycle, so there is nothing to vote
    /// on.
    /// </summary>
    /// <param name="code">The submission's code.</param>
    /// <returns>The failure.</returns>
    public static Error NotUnderReview(ShortCode code) => Error.Conflict(
        "Submission.NotUnderReview",
        $"Submission {code} is not currently up for voting.");

    private static string Describe(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft => "not yet accepted",
        SubmissionStatus.Queued => "waiting for the next cycle",
        SubmissionStatus.UnderReview => "being voted on",
        SubmissionStatus.Decided => "already decided",
        SubmissionStatus.Withdrawn => "withdrawn",
        _ => status.ToString(),
    };

    private static string Describe(SubmissionOutcome outcome) => outcome switch
    {
        SubmissionOutcome.Approved => "approved",
        SubmissionOutcome.Rejected => "rejected",
        SubmissionOutcome.Skipped => "skipped for lack of votes",
        _ => outcome.ToString(),
    };
}
