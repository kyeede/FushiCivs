using System.Globalization;

using Discord;

using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Builds the previous and next buttons under a paginated list, and the line
/// that says where in the list the reader is.
/// </summary>
/// <remarks>
/// The buttons carry the page they navigate to in their own custom identifier,
/// so pressing one re-runs the query for that page rather than reading from a
/// cached result set. That costs a query per press and buys correctness: a list
/// paged through slowly still reflects what is in the database now, and no state
/// has to survive a restart.
/// </remarks>
internal static class Pager
{
    /// <summary>
    /// Describes the reader's position in a list.
    /// </summary>
    /// <param name="info">The page's position and size.</param>
    /// <returns>The text.</returns>
    public static string Position(PageInfo info) => string.Create(
        CultureInfo.InvariantCulture,
        $"Page {info.Number} of {info.TotalPages} · {info.TotalCount} total");

    /// <summary>
    /// Builds the navigation row for a page.
    /// </summary>
    /// <remarks>
    /// A button that cannot go anywhere is disabled rather than hidden, so the
    /// row keeps its shape as the reader moves through the list and the next
    /// button does not slide under the cursor.
    /// <br/>
    /// A single-page list still gets a row, unlike under embeds where it got
    /// none — but only the dismiss button, because a panel a reader cannot page
    /// through is still a panel they should be able to close.
    /// </remarks>
    /// <param name="info">The page's position.</param>
    /// <param name="idForPage">Builds the custom identifier for a given page.</param>
    /// <returns>The component row.</returns>
    public static ActionRowBuilder Navigation(PageInfo info, Func<int, string> idForPage)
    {
        ArgumentNullException.ThrowIfNull(idForPage);

        ButtonBuilder dismiss = new("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary);

        if (info.TotalPages <= 1)
        {
            return Layout.Actions(dismiss);
        }

        return Layout.Actions(
            new ButtonBuilder(
                "Previous",
                idForPage(info.Number - 1),
                ButtonStyle.Secondary,
                isDisabled: !info.HasPrevious),
            new ButtonBuilder(
                "Next",
                idForPage(info.Number + 1),
                ButtonStyle.Secondary,
                isDisabled: !info.HasNext),
            dismiss);
    }
}
