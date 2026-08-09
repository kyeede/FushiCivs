using Fushi.Core.Errors;
using Fushi.Core.Identifiers;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// The failures this layer produces on its own, before a request is dispatched.
/// </summary>
/// <remarks>
/// Almost every refusal a user sees comes from a handler, which is where the
/// rules live. The exceptions are the few things a module has to establish before
/// it can build a request at all — chiefly that a short code typed by hand is
/// actually a short code, for the commands whose request carries a parsed
/// <see cref="ShortCode"/> rather than the raw string.
/// <br/>
/// They are phrased like the application layer's errors, and carry an
/// <c>Interaction.</c> prefix so that a code appearing in a log is traceable to
/// the layer that produced it.
/// </remarks>
internal static class Codes
{
    /// <summary>
    /// Reports that what was typed is not a short code.
    /// </summary>
    /// <param name="value">What the user typed.</param>
    /// <returns>The error to report.</returns>
    public static Error Malformed(string value) => Error.Validation(
        "Interaction.MalformedCode",
        $"`{value}` is not a valid code. A code is {ShortCode.LENGTH} characters, and the "
        + "autocomplete on this option will find one for you — start typing part of a code "
        + "or a title.");
}
