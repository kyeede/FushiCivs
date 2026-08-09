using System.Globalization;

using Discord;

using Fushi.Application.Features.Votes;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Renders the private acknowledgement a voter receives.
/// </summary>
/// <remarks>
/// The buttons a panel votes with live on the review message itself, built by
/// <see cref="SubmissionViews.Review"/>. Under components v2 a message's controls
/// are part of its body rather than a tray attached to it, so there is nothing
/// left here to build them separately from.
/// </remarks>
internal static class VoteViews
{
    /// <summary>
    /// Renders the confirmation shown to whoever just voted.
    /// </summary>
    /// <remarks>
    /// Shows the running tally, which is public information, alongside the
    /// caller's own choice, which is not. That is safe only because this message
    /// is always ephemeral.
    /// </remarks>
    /// <param name="receipt">What the vote did.</param>
    /// <param name="offerComment">
    /// Whether to offer the button that attaches a comment. Absent once a comment
    /// has been written, since the same button would only overwrite it.
    /// </param>
    /// <returns>The message.</returns>
    public static MessageComponent Receipt(VoteReceiptModel receipt, bool offerComment)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        // Stated as a number of votes rather than a percentage gap, because the
        // question a voter has after voting is how many more people are needed,
        // and that is a countable thing.
        string needed = receipt.ApprovalsNeeded <= 0
            ? "passing as it stands"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{receipt.ApprovalsNeeded} more approval(s)");

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading(receipt.WasRevision ? "Vote changed" : "Vote recorded"),
            Layout.Text($"You voted **{Display.Of(receipt.Choice)}** on `{receipt.Code}`."),
            Layout.Rule(),
            Layout.Fields(
                ("Tally", Display.Of(receipt.Tally)),
                ("Approval", string.Create(CultureInfo.InvariantCulture, $"{receipt.ApprovalPercentage}%")),
                ("Still needed", needed)),
        ];

        if (offerComment)
        {
            parts.Add(Layout.Rule());
            parts.Add(Layout.Actions(new ButtonBuilder(
                "Add a comment",
                ComponentIds.VoteComment(receipt.Choice, receipt.Code),
                ButtonStyle.Secondary)));
        }

        return Layout.Panel(Palette.Success, [.. parts]);
    }
}
