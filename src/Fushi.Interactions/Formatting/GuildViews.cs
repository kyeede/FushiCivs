using System.Globalization;
using System.Text;

using Discord;

using Fushi.Application.Features.Guilds;
using Fushi.Application.Features.Permissions;
using Fushi.Core.Entities.Guilds;
using Fushi.Core.Utilities;
using Fushi.Core.Utilities.Paging;
using Fushi.Interactions.Components;

namespace Fushi.Interactions.Formatting;

/// <summary>
/// Renders a guild's configuration and its voting grants.
/// </summary>
internal static class GuildViews
{
    /// <summary>
    /// Renders everything <c>/config show</c> reports.
    /// </summary>
    /// <remarks>
    /// Grouped under subheadings and rules rather than laid out as inline fields,
    /// which components v2 has no equivalent of. That turns out to suit the
    /// content better: routing, passing rules, and scheduling are three separate
    /// decisions a moderator makes, and reading them as three blocks is closer to
    /// how they are configured than a grid of twelve boxes was.
    /// </remarks>
    /// <param name="model">The guild's current settings.</param>
    /// <param name="navigation">
    /// The buttons leading into each part of the configuration, where the reader
    /// is allowed to change it.
    /// </param>
    /// <returns>The message.</returns>
    public static MessageComponent Settings(
        GuildSettingsModel model,
        ActionRowBuilder? navigation = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<IMessageComponentBuilder> parts =
        [
            Layout.Heading(model.IsOperational ? "Configuration" : "Configuration — not ready"),
            Layout.Text(Readiness(model)),
            Layout.Rule(),
            Layout.Subheading("Channels"),
            Layout.Fields(
                ("Intake", Display.Channel(model.IntakeChannelId)),
                ("Review", Display.Channel(model.ReviewChannelId)),
                ("Results", Display.Channel(model.ResultsChannelId)),
                ("Archive", Display.Channel(model.ArchiveChannelId)),
                ("Log", Display.Channel(model.LogChannelId))),
            Layout.Gap(),
            Layout.Subheading("Passing"),
            Layout.Fields(
                (
                    "Threshold",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{model.ApprovalPercentage}% of at least {model.Quorum} deciding vote(s)")),
                ("Abstain", Tick(model.AllowAbstain)),
                ("Self-vote", Tick(model.AllowSelfVote)),
                ("Change vote", Tick(model.AllowVoteChange)),
                ("Voters", GrantCount(model.VotingGrantCount))),
            Layout.Gap(),
            Layout.Subheading("Schedule"),
            Layout.Fields(
                ("Days", Display.Of(model.Days)),
                ("Window", Display.Window(model.OpensAt, model.ClosesAt, model.TimeZoneId))),
        ];

        if (model.NextOpensAt is { } opens)
        {
            parts.Add(Layout.Text(
                $"**Next cycle** · {MentionUtility.Timestamp(opens, TimestampStyle.Relative)} "
                + $"({MentionUtility.Timestamp(opens, TimestampStyle.ShortDateTime)})"));
        }

        if (!model.IsTimeZoneKnown)
        {
            // Worth its own block rather than a footnote: every scheduled instant
            // is being computed in UTC while this holds, so the times above are
            // not the times the guild configured.
            parts.Add(Layout.Rule());
            parts.Add(Layout.Text(
                $"`{model.TimeZoneId}` is not known to this machine, so the schedule is being "
                + "resolved in UTC. Pick a zone under **Schedule**."));
        }

        if (navigation is not null)
        {
            parts.Add(Layout.Rule());
            parts.Add(navigation);
        }

        return Layout.Panel(
            model.IsOperational ? Palette.Success : Palette.Caution,
            [.. parts]);
    }

    /// <summary>
    /// Renders a page of voting grants, each row able to revoke itself.
    /// </summary>
    /// <param name="page">The page to render.</param>
    /// <param name="navigation">The paging buttons.</param>
    /// <returns>The message.</returns>
    public static MessageComponent Permissions(
        Page<VotingPermissionModel> page,
        ActionRowBuilder navigation)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Info.IsEmpty)
        {
            return Layout.Panel(
                Palette.Caution,
                Layout.Heading("Voting rights"),
                Layout.Text(
                    "Nobody has been granted the right to vote. Voting is deny-by-default, so "
                    + "until a grant exists no vote can be cast — not even by an administrator. "
                    + "Use `/voter grant`."),
                navigation);
        }

        List<IMessageComponentBuilder> parts = [Layout.Heading("Voting rights"), Layout.Rule()];

        foreach (VotingPermissionModel grant in page)
        {
            StringBuilder line = new(160);
            string kind = grant.Scope == VotingPermissionScope.Role ? "Role" : "User";

            string granted = MentionUtility.Timestamp(grant.GrantedAt, TimestampStyle.Relative);

            line.Append(CultureInfo.InvariantCulture, $"**{kind}** {grant.Mention}\n");
            line.Append(
                CultureInfo.InvariantCulture,
                $"-# granted by {MentionUtility.User(grant.GrantedBy)} {granted}");

            if (!string.IsNullOrWhiteSpace(grant.Note))
            {
                line.Append(CultureInfo.InvariantCulture, $"\n> {Layout.Clamp(grant.Note, 120)}");
            }

            parts.Add(Layout.Row(
                line.ToString(),
                "Revoke",
                ComponentIds.Revoke(grant.Scope, grant.TargetId),
                ButtonStyle.Danger));
        }

        parts.Add(Layout.Note(Pager.Position(page.Info)));
        parts.Add(navigation);

        return Layout.Panel(Palette.Neutral, [.. parts]);
    }

    private static string Readiness(GuildSettingsModel model)
    {
        if (model.IsOperational)
        {
            return "Cycles can open.";
        }

        if (!model.IsEnabled)
        {
            return "**Disabled.** No cycle will open until `/config enable` is run.";
        }

        // IsOperational is enabled plus a ready channel pair, so reaching here
        // with IsEnabled true means the routing is what is missing.
        return "**Not ready.** An intake channel and a review channel are both required "
            + "before a cycle can open. Set them with `/config channels`.";
    }

    private static string Tick(bool allowed) => allowed ? "allowed" : "not allowed";

    private static string GrantCount(int count) => count switch
    {
        0 => "none — `/voter grant` first",
        1 => "1 grant",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count} grants"),
    };
}
