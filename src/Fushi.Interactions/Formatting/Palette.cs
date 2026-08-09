using Discord;

using Fushi.Core.Entities.Cycles;
using Fushi.Core.Entities.Submissions;
using Fushi.Core.Errors;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// The colours an embed is given, and the rule for choosing one.
/// </summary>
/// <remarks>
/// Colour is the only part of an embed a reader takes in before reading it, so
/// it carries meaning rather than decoration: green approved, red rejected,
/// amber undecided. Choosing the colour in one place is what keeps "rejected"
/// and "failed" the same red everywhere, which is what makes the shorthand
/// learnable in the first place.
/// </remarks>
internal static class Palette
{
    /// <summary>The colour of a neutral, informational embed.</summary>
    public static Color Neutral { get; } = new(0x5865F2);

    /// <summary>The colour of an embed reporting something that succeeded.</summary>
    public static Color Success { get; } = new(0x57F287);

    /// <summary>The colour of an embed reporting a refusal or a failure.</summary>
    public static Color Failure { get; } = new(0xED4245);

    /// <summary>The colour of an embed reporting a caution or a pending state.</summary>
    public static Color Caution { get; } = new(0xFEE75C);

    /// <summary>The colour of an embed whose subject is inert or concluded.</summary>
    public static Color Muted { get; } = new(0x99AAB5);

    /// <summary>
    /// Chooses the colour for an embed reporting a failure.
    /// </summary>
    /// <remarks>
    /// A validation problem and a refusal are amber rather than red: the user can
    /// fix the first by retyping and the second is a rule working as intended,
    /// whereas red is reserved for "this did not work and you cannot fix it by
    /// trying again".
    /// </remarks>
    /// <param name="type">The kind of error being reported.</param>
    /// <returns>The colour to give the embed.</returns>
    public static Color For(ErrorType type) => type switch
    {
        ErrorType.None => Neutral,
        ErrorType.Validation or ErrorType.Forbidden => Caution,
        ErrorType.NotFound => Muted,
        ErrorType.Conflict or ErrorType.Unexpected => Failure,
        _ => Failure,
    };

    /// <summary>
    /// Chooses the colour representing a submission's decision.
    /// </summary>
    /// <param name="outcome">
    /// The decision, or <see langword="null"/> when none has been reached.
    /// </param>
    /// <returns>The colour to give the embed.</returns>
    public static Color For(SubmissionOutcome? outcome) => outcome switch
    {
        SubmissionOutcome.Approved => Success,
        SubmissionOutcome.Rejected => Failure,
        SubmissionOutcome.Skipped => Muted,
        _ => Neutral,
    };

    /// <summary>
    /// Chooses the colour representing a cycle's progress.
    /// </summary>
    /// <param name="status">The cycle's status.</param>
    /// <returns>The colour to give the embed.</returns>
    public static Color For(CycleStatus status) => status switch
    {
        CycleStatus.Open => Success,
        CycleStatus.Scheduled => Neutral,
        CycleStatus.Closed => Caution,
        CycleStatus.Cancelled => Failure,
        CycleStatus.Finalised => Muted,
        _ => Muted,
    };
}
