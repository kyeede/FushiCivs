using Discord;

using Fushi.Core.Entities.Guilds;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Builds the panels voting rights are handed out and taken back through.
/// </summary>
/// <remarks>
/// A grant covers either a user or a role, never both, which as a pair of optional
/// slash command options meant the command had to be refused whenever somebody
/// filled in both or neither. Discord has a menu that selects users and roles
/// together, so the ambiguity can be removed rather than validated: there is one
/// control, it returns one kind of thing at a time, and the failure it used to
/// produce is no longer expressible.
/// </remarks>
internal static class VoterPanels
{
    /// <summary>
    /// The most grants one use of the menu may hand out.
    /// </summary>
    /// <remarks>
    /// Bulk granting is the normal case when a panel is first assembled. The
    /// ceiling exists so a single interaction cannot turn into an unbounded run of
    /// writes, and because a mistake picking twelve people at once is harder to
    /// unpick than a mistake picking three.
    /// </remarks>
    public const int MAX_TARGETS = 10;

    /// <summary>
    /// Builds the panel that hands out voting rights.
    /// </summary>
    /// <param name="notice">What just happened, when something did.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Grant(string? notice = null)
    {
        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Grant voting rights"),
            Layout.Text(
                "Voting is deny-by-default and separate from Discord's permissions — an "
                + "administrator cannot vote without a grant. Pick the users or roles that sit "
                + "on the review panel."),
        ];

        if (notice is not null)
        {
            parts.Add(Layout.Text(notice));
        }

        parts.Add(Layout.Rule());
        parts.Add(new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
            .WithCustomId(ComponentIds.VOTER_GRANT_PICK)
            .WithType(ComponentType.MentionableSelect)
            .WithPlaceholder("Choose users or roles")
            .WithMinValues(1)
            .WithMaxValues(MAX_TARGETS)));
        parts.Add(Layout.Note(
            "Granting a role covers everyone who holds it, now and in future. Granting somebody "
            + "who already has rights changes nothing."));
        parts.Add(Layout.Actions(
            new ButtonBuilder("Who can vote", ComponentIds.VOTER_LIST, ButtonStyle.Secondary),
            new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary)));

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    /// <summary>
    /// Builds the panel that takes voting rights back.
    /// </summary>
    /// <remarks>
    /// One target at a time, unlike granting. Revoking a role is confirmed first
    /// because a single grant can be the reason a great many people can vote, and
    /// a confirmation that had to speak for a mixed selection could not say how
    /// many people any of it affected.
    /// </remarks>
    /// <param name="notice">What just happened, when something did.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Revoke(string? notice = null)
    {
        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Remove voting rights"),
            Layout.Text(
                "Grants are additive, so removing one only takes away what it gave. Somebody "
                + "holding a grant of their own keeps it when a role they are in loses its."),
        ];

        if (notice is not null)
        {
            parts.Add(Layout.Text(notice));
        }

        parts.Add(Layout.Rule());
        parts.Add(new ActionRowBuilder().WithSelectMenu(new SelectMenuBuilder()
            .WithCustomId(ComponentIds.VOTER_REVOKE_PICK)
            .WithType(ComponentType.MentionableSelect)
            .WithPlaceholder("Choose a user or a role")
            .WithMinValues(1)
            .WithMaxValues(1)));
        parts.Add(Layout.Actions(
            new ButtonBuilder("Who can vote", ComponentIds.VOTER_LIST, ButtonStyle.Secondary),
            new ButtonBuilder("Dismiss", ComponentIds.DISMISS, ButtonStyle.Secondary)));

        return Layout.Panel(Palette.Caution, [.. parts]);
    }

    /// <summary>
    /// Builds the reply confirming what a grant did, offering a note where one
    /// can still be attached.
    /// </summary>
    /// <remarks>
    /// The note is offered only for a single grant. It is prose, so a modal is the
    /// right control for it, and a modal opened for several grants at once would
    /// have to ask for one note and quietly apply it to all of them.
    /// </remarks>
    /// <param name="summary">What was granted, already rendered.</param>
    /// <param name="single">The one grant made, or nothing when there were several.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Granted(
        string summary,
        (VotingPermissionScope Scope, ulong TargetId)? single)
    {
        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading("Voting granted"),
            Layout.Text(summary),
        ];

        List<ButtonBuilder> buttons = [];

        if (single is { } grant)
        {
            buttons.Add(new ButtonBuilder(
                "Add a note",
                ComponentIds.GrantNote(grant.Scope, grant.TargetId),
                ButtonStyle.Secondary));
        }

        buttons.Add(new ButtonBuilder(
            "Grant more",
            ComponentIds.VOTER_GRANT,
            ButtonStyle.Secondary));
        buttons.Add(new ButtonBuilder(
            "Who can vote",
            ComponentIds.VOTER_LIST,
            ButtonStyle.Secondary));

        parts.Add(Layout.Actions([.. buttons]));

        return Layout.Panel(Palette.Success, [.. parts]);
    }
}
