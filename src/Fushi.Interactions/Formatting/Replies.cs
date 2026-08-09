using Discord;

using Fushi.Core.Errors;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Turns an <see cref="Error"/> into the message a user reads, and builds the
/// small confirmations and prompts that are not worth a view of their own.
/// </summary>
/// <remarks>
/// Handlers return failure as a value carrying a description written for a
/// person, so rendering one is a matter of choosing a colour and a heading
/// rather than translating a code. The mapping from <see cref="ErrorType"/> to
/// heading lives here so that "not found" is phrased identically whether it was
/// a cycle, a submission, or a grant that could not be found.
/// </remarks>
internal static class Replies
{
    /// <summary>
    /// Renders a failed result.
    /// </summary>
    /// <param name="error">The error to report.</param>
    /// <returns>The message to reply with.</returns>
    public static MessageComponent Error(Error error) => Layout.Panel(
        Palette.For(error.Type),
        Layout.Heading(Heading(error.Type)),
        Layout.Text(error.Description),
        Layout.Note($"Code `{error.Code}`"));

    /// <summary>
    /// Renders a completed action.
    /// </summary>
    /// <param name="title">What happened.</param>
    /// <param name="description">The detail, if any is worth stating.</param>
    /// <returns>The message to reply with.</returns>
    public static MessageComponent Success(string title, string? description = null) =>
        description is null
            ? Layout.Panel(Palette.Success, Layout.Heading(title))
            : Layout.Panel(
                Palette.Success,
                Layout.Heading(title),
                Layout.Text(description));

    /// <summary>
    /// Renders a question that must be answered before something happens, with
    /// the buttons that answer it.
    /// </summary>
    /// <remarks>
    /// The prompt and its buttons are one object now, where under embeds they
    /// were an embed plus a detached component row. That is not only tidier: the
    /// buttons sit inside the accented container, so a destructive confirmation
    /// reads as one warning rather than as a message with some controls beneath
    /// it.
    /// </remarks>
    /// <param name="title">The action awaiting confirmation.</param>
    /// <param name="description">What the action will do, stated plainly.</param>
    /// <param name="confirmId">The custom identifier of the confirming button.</param>
    /// <param name="confirmLabel">The confirming button's label.</param>
    /// <param name="destructive">
    /// Whether the action cannot be undone, which decides whether the confirming
    /// button is red.
    /// </param>
    /// <returns>The message to reply with.</returns>
    public static MessageComponent Confirm(
        string title,
        string description,
        string confirmId,
        string confirmLabel,
        bool destructive = false) =>
        Layout.Panel(
            Palette.Caution,
            Layout.Heading(title),
            Layout.Text(description),
            Layout.Rule(),
            Layout.Actions(
                new ButtonBuilder(
                    confirmLabel,
                    confirmId,
                    destructive ? ButtonStyle.Danger : ButtonStyle.Primary),
                new ButtonBuilder("Cancel", ComponentIds.DISMISS, ButtonStyle.Secondary)));

    private static string Heading(ErrorType type) => type switch
    {
        ErrorType.Validation => "That command could not be read",
        ErrorType.NotFound => "Nothing found",
        ErrorType.Conflict => "That cannot happen right now",
        ErrorType.Forbidden => "Not permitted",
        ErrorType.None or ErrorType.Unexpected => "Something went wrong",
        _ => "Something went wrong",
    };
}
